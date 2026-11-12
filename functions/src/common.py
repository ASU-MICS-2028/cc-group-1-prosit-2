"""Shared by both appointment-event consumers (Lab 7).

The API publishes one event per appointment change to an SNS topic. SNS copies it to one SQS
queue per consumer, so each consumer gets its own copy, its own retries and its own
dead-letter queue, and neither knows the other exists.

Nothing here talks to the database: the event carries what a consumer needs, so the functions
sit outside the VPC, need no database password, and keep working while the API is down.
"""

import json
import logging
import os
import time
from datetime import datetime, timezone

import boto3
from botocore.exceptions import ClientError

log = logging.getLogger()
log.setLevel(logging.INFO)

EVENT_TYPES = {
    "appointment.created",
    "appointment.rescheduled",
    "appointment.cancelled",
    "appointment.updated",
    "appointment.referral_answered",
}
# Care events share the topic: a new prescription (texted to the patient) and an abnormal result (an alert for clinicians).
CARE_TYPES = {"prescription.created": "prescription", "result.abnormal": "result"}
IDEMPOTENCY_DAYS = 30

_clients = {}


def client(name):
    """One boto3 client per service, made on first use (tests replace them with fakes)."""
    if name not in _clients:
        _clients[name] = boto3.client(name)
    return _clients[name]


class BadEvent(ValueError):
    """The message can never be processed (malformed). It is retried, then lands in the DLQ."""


def parse(body):
    """The event inside one SQS message. Anything malformed raises BadEvent."""
    try:
        event = json.loads(body)
    except (TypeError, json.JSONDecodeError) as err:
        raise BadEvent(f"not JSON: {err}") from err
    if not isinstance(event, dict):
        raise BadEvent("not a JSON object")
    kind = event.get("type")
    if kind not in EVENT_TYPES and kind not in CARE_TYPES:
        raise BadEvent(f"unknown event type {kind!r}")
    if not event.get("eventId"):
        raise BadEvent("no eventId")
    if kind in CARE_TYPES:
        body = event.get(CARE_TYPES[kind])
        if not isinstance(body, dict) or not body.get("id"):
            raise BadEvent(f"no {CARE_TYPES[kind]} id")
        return event
    appt = event.get("appointment")
    if not isinstance(appt, dict) or not appt.get("id") or not appt.get("scheduledAt"):
        raise BadEvent("no appointment id or time")
    try:
        when(appt["scheduledAt"])
    except ValueError as err:
        raise BadEvent(f"bad scheduledAt: {err}") from err
    return event


def when(iso):
    """An ISO-8601 time from the API as an aware UTC datetime."""
    value = datetime.fromisoformat(iso.replace("Z", "+00:00"))
    if value.tzinfo is None:
        raise ValueError("time has no offset")
    return value.astimezone(timezone.utc)


def masked(phone):
    """+233241234567 -> +233*****4567, so the logs never hold a whole number."""
    if not phone:
        return "(no phone)"
    return phone[:4] + "*" * max(len(phone) - 8, 0) + phone[-4:]


def ghana_time(iso):
    """'Mon 16 Nov, 10:00' in Ghana time (GMT all year, no daylight saving)."""
    return when(iso).strftime("%a %d %b, %H:%M")


def claim(consumer, event_id):
    """
    Records that this consumer is handling this event. False when it already did: SNS and SQS
    deliver at least once, so the same event can arrive twice and must not send two texts.
    """
    table = os.environ["IDEMPOTENCY_TABLE"]
    try:
        client("dynamodb").put_item(
            TableName=table,
            Item={
                "pk": {"S": f"{consumer}#{event_id}"},
                "at": {"S": datetime.now(timezone.utc).isoformat()},
                "expires": {"N": str(int(time.time()) + IDEMPOTENCY_DAYS * 86400)},
            },
            ConditionExpression="attribute_not_exists(pk)",
        )
        return True
    except ClientError as err:
        if err.response["Error"]["Code"] == "ConditionalCheckFailedException":
            return False
        raise


def release(consumer, event_id):
    """Undo a claim when handling failed, so the retry is not mistaken for a duplicate."""
    try:
        client("dynamodb").delete_item(
            TableName=os.environ["IDEMPOTENCY_TABLE"], Key={"pk": {"S": f"{consumer}#{event_id}"}}
        )
    except ClientError:
        log.exception("could not release claim %s#%s", consumer, event_id)


def each_message(event, consumer, handle):
    """
    Runs handle(parsed_event) for every SQS record. Failed records are reported back one by one
    (partial batch response) so only they are retried; after the queue's maxReceiveCount they
    move to the dead-letter queue instead of vanishing.
    """
    failures = []
    for record in event.get("Records", []):
        message_id = record.get("messageId")
        try:
            parsed = parse(record.get("body"))
        except BadEvent as err:
            log.error(json.dumps({"consumer": consumer, "result": "bad_event", "messageId": message_id, "error": str(err)}))
            failures.append({"itemIdentifier": message_id})
            continue

        event_id = parsed["eventId"]
        if not claim(consumer, event_id):
            log.info(json.dumps({"consumer": consumer, "result": "duplicate_skipped", "eventId": event_id}))
            continue
        try:
            handle(parsed)
        except Exception:  # noqa: BLE001 - any failure is retried, then dead-lettered
            log.exception("%s failed on %s", consumer, event_id)
            release(consumer, event_id)
            failures.append({"itemIdentifier": message_id})
    return {"batchItemFailures": failures}


def upsert_schedule(name, at, payload, dead_letter_arn=None):
    """A one-off EventBridge Scheduler schedule that calls this same function with `payload` at `at` (UTC).
    The same name again moves it, so a moved visit never leaves a stale timer behind."""
    args = {
        "Name": name,
        "GroupName": os.environ["SCHEDULE_GROUP"],
        "ScheduleExpression": f"at({at.strftime('%Y-%m-%dT%H:%M:%S')})",
        "ScheduleExpressionTimezone": "UTC",
        "FlexibleTimeWindow": {"Mode": "OFF"},
        "ActionAfterCompletion": "DELETE",
        "Target": {
            "Arn": os.environ["SELF_ARN"],
            "RoleArn": os.environ["SCHEDULER_ROLE_ARN"],
            "Input": json.dumps(payload),
            "RetryPolicy": {"MaximumRetryAttempts": 3, "MaximumEventAgeInSeconds": 3600},
        },
    }
    if dead_letter_arn:
        # still failing after the retries: kept (and alarmed on) instead of vanishing
        args["Target"]["DeadLetterConfig"] = {"Arn": dead_letter_arn}
    if os.environ.get("EVENTS_KMS_KEY_ARN"):
        args["KmsKeyArn"] = os.environ["EVENTS_KMS_KEY_ARN"]
    scheduler = client("scheduler")
    try:
        scheduler.create_schedule(**args)
    except ClientError as err:
        if err.response["Error"]["Code"] != "ConflictException":
            raise
        scheduler.update_schedule(**args)


def delete_schedule(name):
    """Remove a pending schedule; one that already ran or never existed is fine."""
    try:
        client("scheduler").delete_schedule(Name=name, GroupName=os.environ["SCHEDULE_GROUP"])
    except ClientError as err:
        if err.response["Error"]["Code"] != "ResourceNotFoundException":
            raise
