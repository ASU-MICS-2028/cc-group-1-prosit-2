"""Consumer 2: tells the receiving clinicians what needs them (Lab 7 Part B).

Subscribed to the SAME events as the reminder function, through its own queue. The SNS
subscription's filter policy lets through only what clinicians act on: referrals and
teleconsults (new, moved, cancelled, answered) and test results that came back not normal.
It knows nothing about the reminder function, and the reminder function knows nothing about
it: either can fail, be redeployed or be removed without touching the other or the API.

The message goes to the clinician-alerts SNS topic (email to the district's referral desk in
this MVP; an SMS or push subscription plugs in the same way). It never carries the patient's
name or phone number: the clinician opens HealthConnect to see the case.

Automatic escalation (the brief's emergency protocol): an emergency referral or teleconsult
also starts a one-off timer (EventBridge Scheduler, ESCALATE_AFTER_MINUTES, 30 by default). A
nurse or doctor accepting or declining it, or the CHV cancelling it, stops the timer. If the
timer fires, nobody has answered: the alert goes out again, marked as unanswered.
"""

import json
import os
from datetime import datetime, timedelta, timezone

from common import client, delete_schedule, each_message, ghana_time, log, upsert_schedule

CONSUMER = "specialist-notify"
WHAT = {"referral": "referral", "teleconsult": "teleconsultation request"}
TEST_KIND = {"rapid": "quick test", "lab": "laboratory test", "imaging": "X-ray or scan"}


def handler(event, context):
    if event.get("action") == "escalate":
        return escalate(event)
    return each_message(event, CONSUMER, on_event)


def escalate_after():
    return timedelta(minutes=int(os.environ.get("ESCALATE_AFTER_MINUTES", "30")))


def escalation_name(appointment_id):
    return f"esc-{appointment_id}"


def message_for(event):
    appt = event["appointment"]
    kind = WHAT.get(appt.get("type"), appt.get("type", "appointment"))
    priority = appt.get("priority", "routine")
    at = ghana_time(appt["scheduledAt"])
    facility = appt.get("facility", "")
    if event["type"] == "appointment.cancelled":
        subject = f"HealthConnect: {kind} cancelled"
        body = f"The {kind} to {facility} for {at} was cancelled by the health volunteer."
    else:
        verb = "moved" if event["type"] == "appointment.rescheduled" else "new"
        subject = f"HealthConnect: {verb} {priority} {kind}"
        if priority == "emergency":
            subject = "URGENT - " + subject
        body = (
            f"A {priority} {kind} to {facility} is {'now ' if verb == 'moved' else ''}booked for {at}.\n"
            "Open HealthConnect > Referrals to accept or decline it."
        )
    return subject[:100], body


def result_message(event):
    r = event["result"]
    kind = TEST_KIND.get(r.get("category"), "test")
    where = f" from {r['facility']}" if r.get("facility") else ""
    subject = f"HealthConnect: {r.get('test', 'test')} result not normal"
    body = (
        f"A {kind} ({r.get('test', 'test')}){where} came back not normal.\n"
        "Open HealthConnect to see the result and the photo of the slip or film, and decide the next step."
    )
    return subject[:100], body


def publish(subject, body, priority):
    topic = os.environ.get("CLINICIAN_TOPIC_ARN")
    if topic:
        client("sns").publish(
            TopicArn=topic,
            Subject=subject,
            Message=body,
            MessageAttributes={"priority": {"DataType": "String", "StringValue": priority}},
        )
    return topic


def on_event(event):
    kind = event["type"]
    if kind == "result.abnormal":
        subject, body = result_message(event)
        topic = publish(subject, body, "urgent")
        log.info(json.dumps({
            "consumer": CONSUMER,
            "result": "result_alerted" if topic else "result_alert_logged",
            "resultId": event["result"]["id"],
            "subject": subject,
        }))
        return
    if kind == "prescription.created":
        return   # for the patient (appointment-reminder)

    appt = event["appointment"]
    if kind == "appointment.referral_answered":
        delete_schedule(escalation_name(appt["id"]))   # someone answered: no escalation
        log.info(json.dumps({
            "consumer": CONSUMER,
            "result": "escalation_stopped",
            "appointmentId": appt["id"],
            "answer": appt.get("referralStatus"),
        }))
        return
    if kind == "appointment.updated":
        return   # done, missed or another facility: nothing for the referral desk

    subject, body = message_for(event)
    topic = publish(subject, body, appt.get("priority", "routine"))
    log.info(json.dumps({
        "consumer": CONSUMER,
        "result": "specialist_notified" if topic else "specialist_notify_logged",
        "appointmentId": appt["id"],
        "eventType": kind,
        "appointmentType": appt.get("type"),
        "priority": appt.get("priority"),
        "visitAt": appt["scheduledAt"],
        "subject": subject,
    }))

    name = escalation_name(appt["id"])
    if kind == "appointment.cancelled":
        delete_schedule(name)
    elif appt.get("priority") == "emergency" and appt.get("referralStatus", "requested") == "requested":
        at = datetime.now(timezone.utc) + escalate_after()
        upsert_schedule(name, at, {"action": "escalate", "appointment": appt})
        log.info(json.dumps({"consumer": CONSUMER, "result": "escalation_scheduled", "appointmentId": appt["id"], "at": at.isoformat()}))


def escalate(payload):
    """The timer fired: an emergency is still unanswered. Alert again, louder."""
    appt = payload["appointment"]
    kind = WHAT.get(appt.get("type"), "referral")
    minutes = int(escalate_after().total_seconds() // 60)
    subject = f"URGENT - UNANSWERED emergency {kind} ({minutes} min)"
    body = (
        f"An emergency {kind} to {appt.get('facility', '')} for {ghana_time(appt['scheduledAt'])} "
        f"has had no answer for {minutes} minutes.\n"
        "Open HealthConnect > Referrals now, or call the health volunteer."
    )
    topic = publish(subject[:100], body, "emergency")
    log.info(json.dumps({
        "consumer": CONSUMER,
        "result": "escalated" if topic else "escalation_logged",
        "appointmentId": appt["id"],
    }))
    return {"statusCode": 200, "result": "escalated"}
