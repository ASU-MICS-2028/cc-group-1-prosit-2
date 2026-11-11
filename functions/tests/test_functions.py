"""Tests for the two appointment-event consumers. AWS is replaced by small fakes: no account needed."""

import json
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest
from botocore.exceptions import ClientError

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

import aes_cfb  # noqa: E402
import appointment_reminder  # noqa: E402
import common  # noqa: E402
import sms  # noqa: E402
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


class FakeProvider:
    """Stands in for the SMS provider's web API: records the request, answers with `status` and `body`."""

    def __init__(self, status=200, body=b'{"status":"success"}'):
        self.status, self.body, self.requests = status, body, []

    def __call__(self, request, timeout):
        self.requests.append({"url": request.full_url, "headers": dict(request.header_items()), "body": json.loads(request.data)})
        if self.status >= 400:
            raise sms.urllib.error.HTTPError(request.full_url, self.status, "refused", {}, None)
        provider = self

        class Response:
            status = provider.status

            def read(self):
                return provider.body

            def __enter__(self):
                return self

            def __exit__(self, *_):
                return False

        return Response()


def use_provider(aws, monkeypatch, name, secret, fake=None):
    class Secrets:
        def get_secret_value(self, SecretId):
            assert SecretId == "arn:secret"
            return {"SecretString": secret}

    aws["secretsmanager"] = Secrets()
    fake = fake or FakeProvider()
    monkeypatch.setattr(sms, "_settings", None)
    monkeypatch.setattr(sms.urllib.request, "urlopen", fake)
    monkeypatch.setenv("SMS_PROVIDER", name)
    monkeypatch.setenv("SMS_SECRET_ARN", "arn:secret")
    return fake


def send(e):
    return appointment_reminder.send_reminder({"appointment": e["appointment"], "patient": e["patient"]})


def test_arkesel_send_uses_the_key_from_secrets_manager(aws, monkeypatch):
    fake = use_provider(aws, monkeypatch, "arkesel", "test-key\n")
    assert send(event())["result"] == "reminder_sent"
    [req] = fake.requests
    assert req["url"] == sms.ARKESEL_URL
    assert req["headers"]["Api-key"] == "test-key"
    assert req["body"]["recipients"] == ["233241234567"]


def test_httpsms_sends_from_the_phone_number_with_the_text_encrypted(aws, monkeypatch, caplog):
    caplog.set_level("INFO")
    secret = json.dumps({"apiKey": "hs-key", "from": "+233200000999", "encryptionKey": "phone passphrase"})
    fake = use_provider(aws, monkeypatch, "httpsms", secret)
    e = event()
    assert send(e)["result"] == "reminder_sent"
    [req] = fake.requests
    assert req["url"] == sms.HTTPSMS_URL
    assert req["headers"]["X-api-key"] == "hs-key"
    body = req["body"]
    assert body["from"] == "+233200000999" and body["to"] == "+233241234567" and body["encrypted"] is True
    assert "Dodowa" not in body["content"]                     # httpSMS's servers cannot read it
    assert aes_cfb.httpsms_decrypt("phone passphrase", body["content"]) == appointment_reminder.reminder_text(e["appointment"])
    assert "+233241234567" not in caplog.text and "hs-key" not in caplog.text


def test_httpsms_without_an_encryption_key_sends_plain_text(aws, monkeypatch):
    fake = use_provider(aws, monkeypatch, "httpsms", json.dumps({"apiKey": "k", "from": "+233200000999"}))
    e = event()
    send(e)
    assert fake.requests[0]["body"]["encrypted"] is False
    assert fake.requests[0]["body"]["content"] == appointment_reminder.reminder_text(e["appointment"])


@pytest.mark.parametrize("status, body", [(401, b""), (500, b"oops"), (200, b'{"status":"error","message":"no phone"}')])
def test_a_refusal_fails_the_send_so_it_is_retried_without_leaking_anything(aws, monkeypatch, status, body):
    use_provider(aws, monkeypatch, "httpsms", json.dumps({"apiKey": "hs-key", "from": "+233200000999"}), FakeProvider(status, body))
    with pytest.raises(sms.SmsError) as err:
        send(event())
    assert "hs-key" not in str(err.value) and "233241234567" not in str(err.value)


def test_a_provider_without_its_secret_only_logs(aws, monkeypatch):
    monkeypatch.setenv("SMS_PROVIDER", "httpsms")
    monkeypatch.delenv("SMS_SECRET_ARN", raising=False)
    monkeypatch.delenv("SMS_API_KEY_SECRET_ARN", raising=False)
    assert send(event())["result"] == "reminder_logged"


def test_a_redriven_send_from_the_dead_letter_queue_is_sent(aws, monkeypatch):
    fake = use_provider(aws, monkeypatch, "httpsms", json.dumps({"apiKey": "k", "from": "+233200000999"}))
    e = event()
    record = {"messageId": "m1", "body": json.dumps({"action": "send", "eventId": e["eventId"], "appointment": e["appointment"], "patient": e["patient"]})}
    assert appointment_reminder.handler({"Records": [record]}, None) == {"batchItemFailures": []}
    assert len(fake.requests) == 1


def test_a_redriven_send_for_a_visit_that_has_passed_is_dropped(aws, monkeypatch):
    fake = use_provider(aws, monkeypatch, "httpsms", json.dumps({"apiKey": "k", "from": "+233200000999"}))
    e = event(hours_ahead=-2)
    record = {"messageId": "m1", "body": json.dumps({"action": "send", "appointment": e["appointment"], "patient": e["patient"]})}
    assert appointment_reminder.handler({"Records": [record]}, None) == {"batchItemFailures": []}
    assert fake.requests == []


def test_schedule_failures_go_to_the_dead_letter_queue(aws, monkeypatch):
    monkeypatch.setenv("REMINDER_DLQ_ARN", "arn:aws:sqs:af-south-1:1:reminders-dlq")
    appointment_reminder.handler(sqs(event()), None)
    [schedule] = aws["scheduler"].schedules.values()
    assert schedule["Target"]["DeadLetterConfig"] == {"Arn": "arn:aws:sqs:af-south-1:1:reminders-dlq"}


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
