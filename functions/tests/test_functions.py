"""Tests for the two appointment-event consumers. AWS is replaced by small fakes: no account needed."""

import json
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest
from botocore.exceptions import ClientError

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

import appointment_reminder  # noqa: E402
import common  # noqa: E402
import specialist_notify  # noqa: E402


def client_error(code):
    return ClientError({"Error": {"Code": code, "Message": code}}, "op")


class FakeDynamo:
    def __init__(self):
        self.items = {}

    def put_item(self, TableName, Item, ConditionExpression):
        if Item["pk"]["S"] in self.items:
            raise client_error("ConditionalCheckFailedException")
        self.items[Item["pk"]["S"]] = Item

    def delete_item(self, TableName, Key):
        self.items.pop(Key["pk"]["S"], None)


class FakeScheduler:
    def __init__(self):
        self.schedules = {}
        self.calls = []

    def create_schedule(self, **args):
        self.calls.append(("create", args["Name"]))
        if args["Name"] in self.schedules:
            raise client_error("ConflictException")
        self.schedules[args["Name"]] = args

    def update_schedule(self, **args):
        self.calls.append(("update", args["Name"]))
        self.schedules[args["Name"]] = args

    def delete_schedule(self, Name, GroupName):
        self.calls.append(("delete", Name))
        if Name not in self.schedules:
            raise client_error("ResourceNotFoundException")
        del self.schedules[Name]


class FakeSns:
    def __init__(self):
        self.published = []

    def publish(self, **args):
        self.published.append(args)


@pytest.fixture
def aws(monkeypatch):
    fakes = {"dynamodb": FakeDynamo(), "scheduler": FakeScheduler(), "sns": FakeSns()}
    monkeypatch.setattr(common, "_clients", fakes)
    monkeypatch.setenv("IDEMPOTENCY_TABLE", "events")
    monkeypatch.setenv("SCHEDULE_GROUP", "reminders")
    monkeypatch.setenv("SELF_ARN", "arn:aws:lambda:af-south-1:1:function:reminder")
    monkeypatch.setenv("SCHEDULER_ROLE_ARN", "arn:aws:iam::1:role/scheduler")
    monkeypatch.setenv("CLINICIAN_TOPIC_ARN", "arn:aws:sns:af-south-1:1:clinicians")
    monkeypatch.delenv("SMS_PROVIDER", raising=False)
    return fakes


def event(kind="appointment.created", hours_ahead=48, version=1, appt_type="follow_up", phone="+233241234567", **appt):
    at = (datetime.now(timezone.utc) + timedelta(hours=hours_ahead)).replace(microsecond=0)
    body = {
        "eventId": f"{kind}:11111111-1111-4111-8111-111111111111:v{version}",
        "type": kind,
        "occurredAt": datetime.now(timezone.utc).isoformat(),
        "appointment": {
            "id": "11111111-1111-4111-8111-111111111111",
            "type": appt_type,
            "scheduledAt": at.isoformat().replace("+00:00", "Z"),
            "facility": "Dodowa Health Centre",
            "priority": "routine",
            "status": "scheduled",
            **appt,
        },
        "patient": {"phone": phone, "preferredLanguage": "tw"},
    }
    return body


def sqs(*bodies):
    return {"Records": [{"messageId": f"m{i}", "body": b if isinstance(b, str) else json.dumps(b)} for i, b in enumerate(bodies)]}


# --- reminder ------------------------------------------------------------------------------

def test_new_visit_schedules_a_reminder_a_day_before(aws):
    e = event(hours_ahead=48)
    result = appointment_reminder.handler(sqs(e), None)
    assert result == {"batchItemFailures": []}
    schedule = aws["scheduler"].schedules["appt-11111111-1111-4111-8111-111111111111"]
    visit = common.when(e["appointment"]["scheduledAt"])
    assert schedule["ScheduleExpression"] == f"at({(visit - timedelta(hours=24)).strftime('%Y-%m-%dT%H:%M:%S')})"
    assert schedule["ActionAfterCompletion"] == "DELETE"
    assert json.loads(schedule["Target"]["Input"])["action"] == "send"


def test_same_event_twice_is_handled_once(aws):
    e = event()
    appointment_reminder.handler(sqs(e, e), None)
    assert [c for c in aws["scheduler"].calls if c[0] == "create"] == [("create", "appt-11111111-1111-4111-8111-111111111111")]


def test_rescheduling_replaces_the_schedule(aws):
    appointment_reminder.handler(sqs(event(hours_ahead=48)), None)
    appointment_reminder.handler(sqs(event("appointment.rescheduled", hours_ahead=72, version=2)), None)
    assert ("update", "appt-11111111-1111-4111-8111-111111111111") in aws["scheduler"].calls
    assert len(aws["scheduler"].schedules) == 1


def test_cancelling_deletes_the_schedule(aws):
    appointment_reminder.handler(sqs(event()), None)
    appointment_reminder.handler(sqs(event("appointment.cancelled", version=2, status="cancelled")), None)
    assert aws["scheduler"].schedules == {}


def test_visit_marked_done_drops_its_reminder(aws):
    appointment_reminder.handler(sqs(event()), None)
    appointment_reminder.handler(sqs(event("appointment.updated", version=2, status="completed")), None)
    assert aws["scheduler"].schedules == {}


def test_cancelling_a_visit_with_no_schedule_is_fine(aws):
    result = appointment_reminder.handler(sqs(event("appointment.cancelled", status="cancelled")), None)
    assert result == {"batchItemFailures": []}


def test_short_notice_visit_is_reminded_now(aws, caplog):
    caplog.set_level("INFO")
    appointment_reminder.handler(sqs(event(hours_ahead=0.5)), None)
    assert aws["scheduler"].schedules == {}
    assert "reminder_logged" in caplog.text
    assert "+233241234567" not in caplog.text   # the number is masked
    assert "+233*****4567" in caplog.text


def test_tomorrow_morning_visit_booked_tonight_is_reminded_an_hour_before(aws):
    e = event(hours_ahead=10)
    appointment_reminder.handler(sqs(e), None)
    visit = common.when(e["appointment"]["scheduledAt"])
    expr = aws["scheduler"].schedules["appt-11111111-1111-4111-8111-111111111111"]["ScheduleExpression"]
    assert expr == f"at({(visit - timedelta(hours=1)).strftime('%Y-%m-%dT%H:%M:%S')})"


def test_patient_without_a_phone_gets_no_schedule(aws):
    appointment_reminder.handler(sqs(event(phone=None)), None)
    assert aws["scheduler"].schedules == {}


def test_malformed_message_is_reported_as_failed_so_it_reaches_the_dlq(aws):
    good = event()
    result = appointment_reminder.handler(sqs("{not json", {"type": "appointment.created"}, good), None)
    assert result == {"batchItemFailures": [{"itemIdentifier": "m0"}, {"itemIdentifier": "m1"}]}
    assert len(aws["scheduler"].schedules) == 1   # the good one in the same batch still went through


def test_a_failure_releases_the_claim_so_the_retry_runs(aws, monkeypatch):
    def broken(**_):
        raise client_error("ThrottlingException")

    monkeypatch.setattr(aws["scheduler"], "create_schedule", broken)
    e = event()
    assert appointment_reminder.handler(sqs(e), None) == {"batchItemFailures": [{"itemIdentifier": "m0"}]}
    assert aws["dynamodb"].items == {}


def test_scheduled_send_logs_the_reminder(aws, caplog):
    caplog.set_level("INFO")
    e = event()
    result = appointment_reminder.handler({"action": "send", "appointment": e["appointment"], "patient": e["patient"]}, None)
    assert result["result"] == "reminder_logged"
    assert "Dodowa Health Centre" in appointment_reminder.reminder_text(e["appointment"])


def test_arkesel_send_uses_the_key_from_secrets_manager(aws, monkeypatch):
    sent = {}

    class Secrets:
        def get_secret_value(self, SecretId):
            return {"SecretString": "test-key\n"}

    class Response:
        status = 200

        def __enter__(self):
            return self

        def __exit__(self, *_):
            return False

    def urlopen(request, timeout):
        sent["headers"] = dict(request.header_items())
        sent["body"] = json.loads(request.data)
        return Response()

    aws["secretsmanager"] = Secrets()
    monkeypatch.setattr(appointment_reminder, "_api_key", None)
    monkeypatch.setattr(appointment_reminder.urllib.request, "urlopen", urlopen)
    monkeypatch.setenv("SMS_PROVIDER", "arkesel")
    monkeypatch.setenv("SMS_API_KEY_SECRET_ARN", "arn:secret")
    e = event()
    result = appointment_reminder.send_reminder({"appointment": e["appointment"], "patient": e["patient"]})
    assert result["result"] == "reminder_sent"
    assert sent["headers"]["Api-key"] == "test-key"
    assert sent["body"]["recipients"] == ["233241234567"]


# --- specialist notify ---------------------------------------------------------------------

def test_referral_notifies_clinicians_without_patient_details(aws):
    specialist_notify.handler(sqs(event(appt_type="referral", priority="urgent")), None)
    [msg] = aws["sns"].published
    assert msg["Subject"] == "HealthConnect: new urgent referral"
    assert "+233" not in msg["Message"]


def test_emergency_is_flagged_urgent(aws):
    specialist_notify.handler(sqs(event(appt_type="teleconsult", priority="emergency")), None)
    assert aws["sns"].published[0]["Subject"].startswith("URGENT - ")


def test_notify_is_idempotent_on_its_own(aws):
    e = event(appt_type="referral")
    specialist_notify.handler(sqs(e, e), None)
    assert len(aws["sns"].published) == 1


def test_both_consumers_handle_the_same_event_independently(aws):
    """Lab 7 Part B: one event, two consumers, each with its own idempotency record."""
    e = event(appt_type="teleconsult")
    appointment_reminder.handler(sqs(e), None)
    specialist_notify.handler(sqs(e), None)
    assert len(aws["scheduler"].schedules) == 1
    assert len(aws["sns"].published) == 1
    assert set(aws["dynamodb"].items) == {f"appointment-reminder#{e['eventId']}", f"specialist-notify#{e['eventId']}"}


def test_cancelled_referral_says_so(aws):
    specialist_notify.handler(sqs(event("appointment.cancelled", appt_type="referral", status="cancelled")), None)
    assert aws["sns"].published[0]["Subject"] == "HealthConnect: referral cancelled"
