"""The hand-written AES against the published test vectors, and the httpSMS message format."""

import base64
import hashlib
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

import aes_cfb  # noqa: E402


def test_fips_197_aes_256_block():
    key = bytes(range(32))
    block = aes_cfb._encrypt_block(aes_cfb._expand_key(key), bytes.fromhex("00112233445566778899aabbccddeeff"))
    assert block.hex() == "8ea2b7ca516745bfeafc49904b496089"


def test_nist_sp800_38a_cfb128_aes256():
    key = bytes.fromhex("603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4")
    iv = bytes.fromhex("000102030405060708090a0b0c0d0e0f")
    plain = bytes.fromhex(
        "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51"
        "30c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710")
    cipher = bytes.fromhex(
        "dc7e84bfda79164b7ecd8486985d386039ffed143b28b1c832113c6331e5407b"
        "df10132415e54b92a13ed0a8267ae2f975a385741ab9cef82031623d55b1e471")
    assert aes_cfb.cfb128_encrypt(key, iv, plain) == cipher
    assert aes_cfb.cfb128_decrypt(key, iv, cipher) == plain


def test_httpsms_format_is_iv_then_ciphertext_with_a_sha256_key():
    iv = bytes(range(16))
    text = "HealthConnect: reminder of your visit (a text longer than one block, with an odd length)."
    payload = base64.b64decode(aes_cfb.httpsms_encrypt("passphrase", text, iv=iv))
    assert payload[:16] == iv and len(payload) == 16 + len(text.encode())
    key = hashlib.sha256(b"passphrase").digest()
    assert aes_cfb.cfb128_decrypt(key, iv, payload[16:]).decode() == text


def test_every_message_gets_a_fresh_iv():
    a, b = aes_cfb.httpsms_encrypt("k", "same text"), aes_cfb.httpsms_encrypt("k", "same text")
    assert a != b
    assert aes_cfb.httpsms_decrypt("k", a) == aes_cfb.httpsms_decrypt("k", b) == "same text"
