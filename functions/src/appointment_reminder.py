"""Consumer 1: texts the patient (Lab 7 Part A): visit reminders, the answer to their referral, and a new prescription.

Triggered two ways:

* by its SQS queue, with appointment events. A new or moved visit gets a one-off EventBridge
  Scheduler schedule named after the appointment, so moving it again replaces the schedule and
  cancelling it deletes the schedule (no stale reminders). A visit booked for less than an hour
  from now is reminded straight away.
* by that schedule when the time comes ({"action": "send", ...}): it sends the text.

Besides reminders it texts the patient when a nurse or doctor accepts or declines their referral
(a declined referral's reminder is dropped), and when a doctor writes them a prescription (its
code, for the pharmacy). Every text is in the language on the patient's record (messages.py).

The text goes out through the provider in SMS_PROVIDER (Arkesel or httpSMS, see sms.py) when its
credentials are in Secrets Manager; otherwise it is only logged, which the lab allows for a sandbox.
"""

import json
import os
from datetime import datetime, timedelta, timezone

import messages
import sms
from common import delete_schedule
from common import each_message, ghana_time, log, masked, when
from common import upsert_schedule as _upsert

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
    kind = event["type"]
    if kind == "result.abnormal":
        return   # for the clinicians (specialist-notify), not a text to the patient
    if kind == "prescription.created":
        return text_patient(event, "prescription", code=event["prescription"]["code"], subject_id=event["prescription"]["id"])
    appt = event["appointment"]
    name = schedule_name(appt["id"])
    if kind == "appointment.referral_answered":
        answer = appt.get("referralStatus")
        if answer == "declined":
            delete_schedule(name)   # the visit will not happen there: no reminder for it
        if answer in ("accepted", "declined"):
            text_patient(event, answer, facility=appt.get("facility", "the clinic"), when=ghana_time(appt["scheduledAt"]), subject_id=appt["id"])
        return
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
    _upsert(name, at, payload, os.environ.get("REMINDER_DLQ_ARN"))


def reminder_text(appt, language=None):
    kind = "teleconsult" if appt.get("type") == "teleconsult" else "visit"
    return messages.text(kind, language, facility=appt.get("facility", "the clinic"), when=ghana_time(appt["scheduledAt"]))


def text_patient(event, kind, subject_id, **values):
    """One text to the patient straight away (no schedule): a referral answer or a prescription code."""
    patient = event.get("patient") or {}
    phone = patient.get("phone")
    if not phone:
        log.info(json.dumps({"consumer": CONSUMER, "result": "no_phone", "kind": kind, "id": subject_id}))
        return
    provider, status = sms.send(phone, messages.text(kind, patient.get("preferredLanguage"), **values))
    log.info(json.dumps({
        "consumer": CONSUMER,
        "result": f"{kind}_{'logged' if provider == 'log' else 'sent'}",
        "provider": provider,
        "id": subject_id,
        "to": masked(phone),
        "language": patient.get("preferredLanguage"),
        "providerStatus": status,
    }))


def send_reminder(payload):
    appt = payload["appointment"]
    patient = payload.get("patient") or {}
    phone = patient.get("phone")
    text = reminder_text(appt, patient.get("preferredLanguage"))
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
