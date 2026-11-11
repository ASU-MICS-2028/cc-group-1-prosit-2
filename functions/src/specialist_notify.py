"""Consumer 2: tells the receiving clinicians about a new referral or teleconsult (Lab 7 Part B).

Subscribed to the SAME appointment events as the reminder function, through its own queue. The
SNS subscription's filter policy only lets referrals and teleconsults through, so ordinary
follow-up visits never reach it. It knows nothing about the reminder function, and the
reminder function knows nothing about it: either can fail, be redeployed or be removed
without touching the other or the API.

The message goes to the clinician-alerts SNS topic (email to the district's referral desk in
this MVP; an SMS or push subscription plugs in the same way). It never carries the patient's
name or phone number: the clinician opens the referrals inbox in the app to see the case.
Emergencies are flagged in the subject so they stand out (the brief's automatic escalation).
"""

import json
import os

from common import client, each_message, ghana_time, log

CONSUMER = "specialist-notify"
WHAT = {"referral": "referral", "teleconsult": "teleconsultation request"}


def handler(event, context):
    return each_message(event, CONSUMER, on_event)


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


def on_event(event):
    appt = event["appointment"]
    subject, body = message_for(event)
    topic = os.environ.get("CLINICIAN_TOPIC_ARN")
    if topic:
        client("sns").publish(
            TopicArn=topic,
            Subject=subject,
            Message=body,
            MessageAttributes={"priority": {"DataType": "String", "StringValue": appt.get("priority", "routine")}},
        )
    log.info(json.dumps({
        "consumer": CONSUMER,
        "result": "specialist_notified" if topic else "specialist_notify_logged",
        "appointmentId": appt["id"],
        "eventType": event["type"],
        "appointmentType": appt.get("type"),
        "priority": appt.get("priority"),
        "visitAt": appt["scheduledAt"],
        "subject": subject,
    }))
