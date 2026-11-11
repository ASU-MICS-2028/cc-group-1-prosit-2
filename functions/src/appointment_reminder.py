"""Consumer 1: reminds the patient of their visit by SMS (Lab 7 Part A).

Triggered two ways:

* by its SQS queue, with appointment events. A new or moved visit gets a one-off EventBridge
  Scheduler schedule named after the appointment, so moving it again replaces the schedule and
  cancelling it deletes the schedule (no stale reminders). A visit booked for less than an hour
  from now is reminded straight away.
* by that schedule when the time comes ({"action": "send", ...}): it sends the text.

The text goes through Arkesel (a Ghanaian SMS gateway) when SMS_PROVIDER=arkesel and the API
key is in Secrets Manager; otherwise it is only logged, which the lab allows for a sandbox.
"""

import json
import os
import urllib.request
from datetime import datetime, timedelta, timezone

from botocore.exceptions import ClientError

from common import client, each_message, ghana_time, log, masked, when

CONSUMER = "appointment-reminder"
REMIND_BEFORE = timedelta(hours=24)
SHORT_NOTICE_BEFORE = timedelta(hours=1)
ARKESEL_URL = "https://sms.arkesel.com/api/v2/sms/send"

_api_key = None


def handler(event, context):
    if event.get("action") == "send":
        return send_reminder(event)
    return each_message(event, CONSUMER, on_event)


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
    provider = os.environ.get("SMS_PROVIDER", "log")
    if provider == "arkesel" and os.environ.get("SMS_API_KEY_SECRET_ARN"):
        status = send_arkesel(phone, text)
        result = "reminder_sent"
    else:
        status = None
        result = "reminder_logged"   # no SMS provider switched on: the lab's simulated reminder
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


def api_key():
    global _api_key
    if _api_key is None:
        secret = client("secretsmanager").get_secret_value(SecretId=os.environ["SMS_API_KEY_SECRET_ARN"])
        _api_key = secret["SecretString"].strip()
    return _api_key


def send_arkesel(phone, text):
    body = json.dumps({
        "sender": os.environ.get("SMS_SENDER_ID", "HealthConnect")[:11],
        "message": text,
        "recipients": [phone.lstrip("+")],
    }).encode()
    request = urllib.request.Request(
        os.environ.get("SMS_URL", ARKESEL_URL),
        data=body,
        method="POST",
        headers={"api-key": api_key(), "Content-Type": "application/json", "Accept": "application/json"},
    )
    with urllib.request.urlopen(request, timeout=10) as response:   # noqa: S310 - fixed https URL
        return response.status
