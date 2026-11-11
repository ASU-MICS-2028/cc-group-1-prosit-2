"""Sending one text message. Three providers, chosen by SMS_PROVIDER:

* log (default): nothing is sent; the caller logs the reminder with the number masked.
* arkesel: Arkesel's bulk SMS API (a Ghanaian gateway, sender name registered with them).
* httpsms: httpSMS (httpsms.com), which sends the text from an Android phone running its app, so
  a text costs what that phone's SIM bundle charges. The text is end-to-end encrypted with the
  passphrase set in the phone's app, so httpSMS's servers only ever see the recipient number.

The credentials are one Secrets Manager secret (SMS_SECRET_ARN), read once per container:
  arkesel: the API key as plain text, or {"apiKey": "..."}
  httpsms: {"apiKey": "...", "from": "+233...", "encryptionKey": "..."}   (encryptionKey optional)
A provider without its secret falls back to log, so a half-configured environment never fails
the reminder. A provider that refuses or cannot be reached raises SmsError: the Lambda fails,
Scheduler or SQS retries, and repeated failures land in the dead-letter queue with an alarm.
Errors never contain the key, the text or the whole number.
"""

import json
import os
import urllib.error
import urllib.request

from aes_cfb import httpsms_encrypt
from common import client

ARKESEL_URL = "https://sms.arkesel.com/api/v2/sms/send"
HTTPSMS_URL = "https://api.httpsms.com/v1/messages/send"
PROVIDERS = ("log", "arkesel", "httpsms")

_settings = None


class SmsError(Exception):
    pass


def secret_arn():
    return os.environ.get("SMS_SECRET_ARN") or os.environ.get("SMS_API_KEY_SECRET_ARN") or ""


def provider():
    chosen = os.environ.get("SMS_PROVIDER", "log").lower()
    if chosen not in PROVIDERS:
        raise SmsError(f"unknown SMS_PROVIDER {chosen!r}")
    return chosen if chosen == "log" or secret_arn() else "log"


def settings():
    global _settings
    if _settings is None:
        raw = client("secretsmanager").get_secret_value(SecretId=secret_arn())["SecretString"].strip()
        _settings = json.loads(raw) if raw.startswith("{") else {"apiKey": raw}
    return _settings


def send(phone, text):
    """Send `text` to `phone` (E.164). Returns the provider used and its HTTP status (None when only logged)."""
    chosen = provider()
    if chosen == "log" or not phone:
        return "log", None
    if chosen == "arkesel":
        return chosen, _post(
            os.environ.get("SMS_URL", ARKESEL_URL),
            {"api-key": settings()["apiKey"]},
            {"sender": os.environ.get("SMS_SENDER_ID", "HealthConnect")[:11], "message": text, "recipients": [phone.lstrip("+")]},
        )
    s = settings()
    if not s.get("from"):
        raise SmsError("httpsms secret has no 'from' number")
    key = s.get("encryptionKey")
    return chosen, _post(
        os.environ.get("SMS_URL", HTTPSMS_URL),
        {"x-api-key": s["apiKey"]},
        {"from": s["from"], "to": phone, "content": httpsms_encrypt(key, text) if key else text, "encrypted": bool(key)},
    )


def _post(url, auth_header, body):
    request = urllib.request.Request(
        url,
        data=json.dumps(body).encode(),
        method="POST",
        headers={**auth_header, "Content-Type": "application/json", "Accept": "application/json"},
    )
    try:
        with urllib.request.urlopen(request, timeout=10) as response:   # noqa: S310 - fixed https URL
            status = response.status
            answer = _status_word(response.read())
    except urllib.error.HTTPError as err:
        raise SmsError(f"SMS provider refused the message (HTTP {err.code})") from None
    except (urllib.error.URLError, TimeoutError) as err:
        raise SmsError(f"SMS provider could not be reached ({type(err).__name__})") from None
    if answer in ("error", "failed", "fail"):
        raise SmsError(f"SMS provider refused the message (status {answer})")
    return status


def _status_word(payload):
    try:
        body = json.loads(payload or b"{}")
    except ValueError:
        return None
    status = body.get("status") if isinstance(body, dict) else None
    return status.lower() if isinstance(status, str) else None
