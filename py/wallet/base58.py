"""
Base58 and Base58Check, ported from Bitcoin Core v30.0 src/base58.cpp.

Base58 writes a big number in 58 symbols: digits and letters minus 0, O, I and l, which look alike.
Each leading zero byte becomes a leading '1', because a number has no leading zeros of its own.
Base58Check appends the first 4 bytes of HASH256(payload) before encoding, so a mistyped
character fails the checksum instead of sending coins to a different address.
"""

from __future__ import annotations

from wallet.hashes import hash256

ALPHABET = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz"
_INDEX = {char: value for value, char in enumerate(ALPHABET)}
_SPACES = " \f\n\r\t\v"  # Core's IsSpace


def encode(data: bytes) -> str:
    zeroes = len(data) - len(data.lstrip(b"\0"))
    number = int.from_bytes(data, "big")
    digits = []
    while number:
        number, remainder = divmod(number, 58)
        digits.append(ALPHABET[remainder])
    return "1" * zeroes + "".join(reversed(digits))


def decode(text: str, max_length: int) -> bytes | None:
    """
    Core's DecodeBase58: leading and trailing whitespace is skipped, any other character outside the
    alphabet fails, and so does a result longer than max_length bytes. None means failure.
    """
    if "\0" in text:
        return None
    body = text.strip(_SPACES)
    zeroes = len(body) - len(body.lstrip("1"))
    if zeroes > max_length:
        return None
    number = 0
    for char in body[zeroes:]:
        if char not in _INDEX:
            return None
        number = number * 58 + _INDEX[char]
    payload = number.to_bytes((number.bit_length() + 7) // 8, "big") if number else b""
    if zeroes + len(payload) > max_length:
        return None
    return b"\0" * zeroes + payload


def encode_check(payload: bytes) -> str:
    return encode(payload + hash256(payload)[:4])


def decode_check(text: str, max_payload_length: int) -> bytes | None:
    """Core's DecodeBase58Check: the checksum must match; returns the payload without it."""
    data = decode(text, max_payload_length + 4)
    if data is None or len(data) < 4:
        return None
    payload, checksum = data[:-4], data[-4:]
    if hash256(payload)[:4] != checksum:
        return None
    return payload
