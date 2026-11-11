"""Consumer 1: reminds the patient of their visit by SMS (Lab 7 Part A).

Triggered two ways:

* by its SQS queue, with appointment events. A new or moved visit gets a one-off EventBridge
  Scheduler schedule named after the appointment, so moving it again replaces the schedule and
  cancelling it deletes the schedule (no stale reminders). A visit booked for less than an hour
  from now is reminded straight away.
* by that schedule when the time comes ({"action": "send", ...}): it sends the text.

The text goes out through the provider in SMS_PROVIDER (Arkesel or httpSMS, see sms.py) when its
credentials are in Secrets Manager; otherwise it is only logged, which the lab allows for a sandbox.
"""

import json
import os
from datetime import datetime, timedelta, timezone

from botocore.exceptions import ClientError

import sms
from common import client, each_message, ghana_time, log, masked, when

CONSUMER = "appointment-reminder"
REMIND_BEFORE = timedelta(hours=24)
SHORT_NOTICE_BEFORE = timedelta(hours=1)


def handler(event, context):
    if event.get("action") == "send":
        return send_reminder(event)
    records = event.get("Records", [])
    resends = [r for r in records if redriven_send(r.get("body"))]
    result = each_message({"Records": [r for r in records if r not in resends]}, CONSUMER, on_event)
    for record in resends:
        try:
            resend(redriven_send(record["body"]))
        except Exception:  # noqa: BLE001 - retried, then back to the dead-letter queue
            log.exception("%s failed to resend %s", CONSUMER, record.get("messageId"))
            result["batchItemFailures"].append({"itemIdentifier": record.get("messageId")})
    return result


def redriven_send(body):
    """A reminder that Scheduler could not deliver lands in the dead-letter queue as its own "send"
    payload. Redriving that queue brings it back here; return it so it is sent, not rejected."""
    try:
        payload = json.loads(body)
    except (TypeError, ValueError):
        return None
    return payload if isinstance(payload, dict) and payload.get("action") == "send" else None


def resend(payload):
    if when(payload["appointment"]["scheduledAt"]) <= datetime.now(timezone.utc):
        log.info(json.dumps({"consumer": CONSUMER, "result": "resend_skipped_visit_passed", "appointmentId": payload["appointment"]["id"]}))
        return
    send_reminder(payload)


def schedule_name(appointment_id):
    return f"appt-{appointment_id}"


def on_event(event):
    appt = event["appointment"]
    name = schedule_name(appt["id"])
    if event["type"] == "appointment.cancelled" or appt.get("status", "scheduled") != "scheduled":
        delete_schedule(name)
        log.info(json.dumps({"consumer": CONSUMER, "result": "reminder_cancelled", "appointmentId": appt["id"]}))
        return

    patient = event.get("patient") or {}
    if not patient.get("phone"):
        # Many rural patients have no phone of their own: the CHV's visit list is their reminder.
        log.info(json.dumps({"consumer": CONSUMER, "result": "no_phone", "appointmentId": appt["id"]}))
        return

    now = datetime.now(timezone.utc)
    visit = when(appt["scheduledAt"])
    if visit <= now:
        log.info(json.dumps({"consumer": CONSUMER, "result": "in_the_past", "appointmentId": appt["id"]}))
        return

    remind_at = visit - REMIND_BEFORE
    if remind_at <= now:
        remind_at = visit - SHORT_NOTICE_BEFORE
    payload = {"action": "send", "eventId": event["eventId"], "appointment": appt, "patient": patient}
    if remind_at <= now + timedelta(minutes=2):
        send_reminder(payload)
        return

    upsert_schedule(name, remind_at, payload)
    log.info(json.dumps({
        "consumer": CONSUMER,
        "result": "reminder_scheduled",
        "appointmentId": appt["id"],
        "eventType": event["type"],
        "to": masked(patient["phone"]),
        "visitAt": appt["scheduledAt"],
        "remindAt": remind_at.isoformat(),
    }))


def upsert_schedule(name, at, payload):
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
    if os.environ.get("REMINDER_DLQ_ARN"):
        # a reminder that still fails after the retries is kept (and alarms) instead of vanishing
        args["Target"]["DeadLetterConfig"] = {"Arn": os.environ["REMINDER_DLQ_ARN"]}
    if os.environ.get("EVENTS_KMS_KEY_ARN"):
        args["KmsKeyArn"] = os.environ["EVENTS_KMS_KEY_ARN"]
    scheduler = client("scheduler")
    try:
        scheduler.create_schedule(**args)
    except ClientError as err:
        if err.response["Error"]["Code"] != "ConflictException":
            raise
        scheduler.update_schedule(**args)   # the visit moved: same name, new time


def delete_schedule(name):
    try:
        client("scheduler").delete_schedule(Name=name, GroupName=os.environ["SCHEDULE_GROUP"])
    except ClientError as err:
        if err.response["Error"]["Code"] != "ResourceNotFoundException":
            raise


def reminder_text(appt):
    what = "phone consultation with a doctor" if appt.get("type") == "teleconsult" else "health visit"
    return (
        f"HealthConnect: reminder of your {what} at {appt.get('facility', 'the clinic')} "
        f"on {ghana_time(appt['scheduledAt'])}. If you cannot come, please tell your health volunteer."
    )


def send_reminder(payload):
    appt = payload["appointment"]
    phone = (payload.get("patient") or {}).get("phone")
    text = reminder_text(appt)
    provider, status = sms.send(phone, text)
    result = "reminder_logged" if provider == "log" else "reminder_sent"   # log: the lab's simulated reminder
    log.info(json.dumps({
        "consumer": CONSUMER,
        "result": result,
        "provider": provider,
        "appointmentId": appt["id"],
        "to": masked(phone),
        "visitAt": appt["scheduledAt"],
        "characters": len(text),
        "providerStatus": status,
    }))
    return {"statusCode": 200, "result": result}
