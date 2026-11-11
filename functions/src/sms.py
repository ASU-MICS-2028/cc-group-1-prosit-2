"""Sending one text message.

Texts go out only when this environment's secret exists in Secrets Manager. Its name is
SMS_SECRET_ID (healthconnect/<env>/sms); the API reads the same secret for sign-in codes. It is
a JSON object; "provider" picks who sends:

  {"provider": "httpsms", "apiKey": "...", "from": "+233...", "encryptionKey": "..."}
      httpSMS (httpsms.com) sends from an Android phone running its app, so a text costs what that
      phone's SIM bundle charges. With encryptionKey (the passphrase set in the phone's app) the
      text is end-to-end encrypted: httpSMS's servers only see the recipient number.
  {"provider": "arkesel", "apiKey": "...", "senderId": "HlthConnect"}
      Arkesel, a Ghanaian bulk SMS gateway (sender name registered with them).

No secret (or provider "log") means nothing is sent and the caller only logs the reminder, with
the number masked. A provider that refuses or cannot be reached raises SmsError: the Lambda
fails, Scheduler or SQS retries, and repeated failures land in the dead-letter queue with an
alarm. Errors never contain the key, the text or the whole number.
"""

import json
import os
import urllib.error
import urllib.request

from botocore.exceptions import ClientError

from aes_cfb import httpsms_encrypt
from common import client

ARKESEL_URL = "https://sms.arkesel.com/api/v2/sms/send"
HTTPSMS_URL = "https://api.httpsms.com/v1/messages/send"
PROVIDERS = ("log", "arkesel", "httpsms")

_settings = None


class SmsError(Exception):
    pass


def settings():
    """This environment's SMS secret as a dict ({"provider": "log"} when there is none). Read once per container."""
    global _settings
    if _settings is None:
        secret_id = os.environ.get("SMS_SECRET_ID")
        if not secret_id:
            _settings = {"provider": "log"}
        else:
            try:
                raw = client("secretsmanager").get_secret_value(SecretId=secret_id)["SecretString"]
            except ClientError as err:
                if err.response["Error"]["Code"] != "ResourceNotFoundException":
                    raise
                raw = '{"provider": "log"}'   # texts not switched on for this environment
            try:
                _settings = json.loads(raw)
            except ValueError:
                raise SmsError("SMS secret is not JSON") from None
        provider = str(_settings.get("provider", "log")).lower()
        if provider not in PROVIDERS:
            raise SmsError(f"unknown SMS provider {provider!r}")
        _settings["provider"] = provider
    return _settings


def send(phone, text):
    """Send `text` to `phone` (E.164). Returns the provider used and its HTTP status (None when only logged)."""
    s = settings()
    provider = s["provider"]
    if provider == "log" or not phone:
        return "log", None
    if not s.get("apiKey"):
        raise SmsError("SMS secret has no apiKey")
    if provider == "arkesel":
        return provider, _post(
            os.environ.get("SMS_URL", ARKESEL_URL),
            {"api-key": s["apiKey"]},
            {"sender": s.get("senderId", "HlthConnect")[:11], "message": text, "recipients": [phone.lstrip("+")]},
        )
    if not s.get("from"):
        raise SmsError("httpsms secret has no 'from' number")
    key = s.get("encryptionKey")
    return provider, _post(
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
