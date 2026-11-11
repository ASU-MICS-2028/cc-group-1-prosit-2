"""AES-256 in CFB mode, written out in plain Python because the Lambda runtime has no AES library.

Only used to encrypt reminder texts for httpSMS's end-to-end encryption, so its servers never see
the text. The format is the one httpSMS's own clients use (httpsms-go CipherService):

    base64( IV (16 random bytes) || AES-256-CFB128(key = SHA-256(passphrase), IV, text) )

CFB only ever runs the block cipher forwards, so only AES encryption is implemented. It is checked
against the FIPS-197 and NIST SP 800-38A test vectors in tests/test_aes_cfb.py.
"""

import base64
import hashlib
import os

_SBOX = bytes.fromhex(
    "637c777bf26b6fc53001672bfed7ab76ca82c97dfa5947f0add4a2af9ca472c0"
    "b7fd9326363ff7cc34a5e5f171d8311504c723c31896059a071280e2eb27b275"
    "09832c1a1b6e5aa0523bd6b329e32f8453d100ed20fcb15b6acbbe394a4c58cf"
    "d0efaafb434d338545f9027f503c9fa851a3408f929d38f5bcb6da2110fff3d2"
    "cd0c13ec5f974417c4a77e3d645d197360814fdc222a908846eeb814de5e0bdb"
    "e0323a0a4906245cc2d3ac629195e479e7c8376d8dd54ea96c56f4ea657aae08"
    "ba78252e1ca6b4c6e8dd741f4bbd8b8a703eb5664803f60e613557b986c11d9e"
    "e1f8981169d98e949b1e87e9ce5528df8ca1890dbfe6426841992d0fb054bb16"
)
_RCON = (0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40)


def _xtime(b):
    b <<= 1
    return (b ^ 0x11B) if b & 0x100 else b


def _expand_key(key):
    """AES-256 key schedule: 15 round keys of 16 bytes."""
    if len(key) != 32:
        raise ValueError("AES-256 needs a 32-byte key")
    words = [list(key[i:i + 4]) for i in range(0, 32, 4)]
    for i in range(8, 60):
        temp = list(words[i - 1])
        if i % 8 == 0:
            temp = temp[1:] + temp[:1]
            temp = [_SBOX[b] for b in temp]
            temp[0] ^= _RCON[i // 8 - 1]
        elif i % 8 == 4:
            temp = [_SBOX[b] for b in temp]
        words.append([a ^ b for a, b in zip(words[i - 8], temp)])
    return [sum(words[r * 4:r * 4 + 4], []) for r in range(15)]


def _encrypt_block(round_keys, block):
    s = [b ^ k for b, k in zip(block, round_keys[0])]
    for r in range(1, 15):
        s = [_SBOX[b] for b in s]                                   # SubBytes
        s = [s[(i + 4 * (i % 4)) % 16] for i in range(16)]          # ShiftRows (column-major state)
        if r != 14:                                                 # MixColumns
            out = []
            for c in range(4):
                a = s[4 * c:4 * c + 4]
                t = a[0] ^ a[1] ^ a[2] ^ a[3]
                out += [a[i] ^ t ^ _xtime(a[i] ^ a[(i + 1) % 4]) for i in range(4)]
            s = out
        s = [b ^ k for b, k in zip(s, round_keys[r])]
    return bytes(s)


def cfb128_encrypt(key, iv, data):
    round_keys = _expand_key(key)
    out = bytearray()
    feedback = iv
    for i in range(0, len(data), 16):
        stream = _encrypt_block(round_keys, feedback)
        chunk = bytes(a ^ b for a, b in zip(data[i:i + 16], stream))
        out += chunk
        feedback = chunk if len(chunk) == 16 else feedback
    return bytes(out)


def cfb128_decrypt(key, iv, data):
    round_keys = _expand_key(key)
    out = bytearray()
    feedback = iv
    for i in range(0, len(data), 16):
        stream = _encrypt_block(round_keys, feedback)
        chunk = data[i:i + 16]
        out += bytes(a ^ b for a, b in zip(chunk, stream))
        feedback = chunk
    return bytes(out)


def httpsms_encrypt(passphrase, text, iv=None):
    """Encrypt a text the way the httpSMS Android app expects to decrypt it."""
    iv = iv if iv is not None else os.urandom(16)
    key = hashlib.sha256(passphrase.encode()).digest()
    return base64.b64encode(iv + cfb128_encrypt(key, iv, text.encode())).decode()


def httpsms_decrypt(passphrase, payload):
    raw = base64.b64decode(payload)
    key = hashlib.sha256(passphrase.encode()).digest()
    return cfb128_decrypt(key, raw[:16], raw[16:]).decode()
