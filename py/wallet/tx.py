"""
Transactions in Bitcoin's wire format (Core: src/primitives/transaction.h), the legacy layout the
wallet builds and signs:

    version (4, LE) | input count (CompactSize) | inputs | output count | outputs | locktime (4, LE)
    input:  prevout txid (32, wire order) | prevout index (4, LE) | scriptSig (CompactSize + bytes) | sequence (4, LE)
    output: value in satoshis (8, LE) | scriptPubKey (CompactSize + bytes)

Parsing also accepts the BIP144 witness layout (marker 00, flag 01 after the version), so the wallet
can read any transaction Core shows it. The txid is HASH256 of the legacy bytes, displayed reversed.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, replace

from wallet.hashes import hash256


def compact_size(n: int) -> bytes:
    if n < 0xFD:
        return bytes([n])
    if n <= 0xFFFF:
        return b"\xfd" + struct.pack("<H", n)
    if n <= 0xFFFF_FFFF:
        return b"\xfe" + struct.pack("<I", n)
    return b"\xff" + struct.pack("<Q", n)


def var_bytes(data: bytes) -> bytes:
    return compact_size(len(data)) + data


@dataclass(frozen=True)
class OutPoint:
    txid: str  # display order, as RPC and explorers show it
    vout: int

    def serialize(self) -> bytes:
        return bytes.fromhex(self.txid)[::-1] + struct.pack("<I", self.vout)


@dataclass(frozen=True)
class TxIn:
    prevout: OutPoint
    script_sig: bytes = b""
    # 0xffffffff: final. It disables nLockTime and opts out of BIP125 replacement; RBF is out of scope.
    sequence: int = 0xFFFF_FFFF
    witness: tuple[bytes, ...] = ()

    def serialize(self) -> bytes:
        return self.prevout.serialize() + var_bytes(self.script_sig) + struct.pack("<I", self.sequence)


@dataclass(frozen=True)
class TxOut:
    value: int  # satoshis
    script_pubkey: bytes

    def serialize(self) -> bytes:
        return struct.pack("<q", self.value) + var_bytes(self.script_pubkey)


@dataclass(frozen=True)
class Transaction:
    inputs: tuple[TxIn, ...]
    outputs: tuple[TxOut, ...]
    version: int = 2  # 2 enables BIP68 relative locktimes; Core's own wallet creates version 2
    locktime: int = 0

    @property
    def has_witness(self) -> bool:
        return any(i.witness for i in self.inputs)

    def serialize_legacy(self) -> bytes:
        return (
            struct.pack("<I", self.version)
            + compact_size(len(self.inputs)) + b"".join(i.serialize() for i in self.inputs)
            + compact_size(len(self.outputs)) + b"".join(o.serialize() for o in self.outputs)
            + struct.pack("<I", self.locktime)
        )

    def serialize(self) -> bytes:
        """Wire bytes: legacy layout, or BIP144 (marker, flag, witnesses before locktime) if any input has a witness."""
        if not self.has_witness:
            return self.serialize_legacy()
        witnesses = b"".join(compact_size(len(i.witness)) + b"".join(var_bytes(item) for item in i.witness)
                             for i in self.inputs)
        legacy = self.serialize_legacy()
        return legacy[:4] + b"\x00\x01" + legacy[4:-4] + witnesses + legacy[-4:]

    def hex(self) -> str:
        return self.serialize().hex()

    @property
    def txid(self) -> str:
        return hash256(self.serialize_legacy())[::-1].hex()

    @property
    def size(self) -> int:
        """For a transaction without witnesses, size, vsize and weight/4 are the same number."""
        return len(self.serialize())

    def with_script_sig(self, index: int, script_sig: bytes) -> Transaction:
        inputs = list(self.inputs)
        inputs[index] = replace(inputs[index], script_sig=script_sig)
        return replace(self, inputs=tuple(inputs))

    @classmethod
    def parse(cls, raw: bytes | str) -> Transaction:
        data = bytes.fromhex(raw) if isinstance(raw, str) else raw
        reader = _Reader(data)
        version = reader.u32()
        input_count = reader.compact_size()
        has_witness = False
        if input_count == 0:  # BIP144: a zero "input count" is the marker; the flag must be 1
            if reader.byte() != 1:
                raise ValueError("unknown transaction serialization flag")
            has_witness = True
            input_count = reader.compact_size()
        inputs = []
        for _ in range(input_count):
            txid = reader.take(32)[::-1].hex()
            vout = reader.u32()
            script_sig = reader.var_bytes()
            inputs.append(TxIn(OutPoint(txid, vout), script_sig, reader.u32()))
        outputs = [TxOut(reader.i64(), reader.var_bytes()) for _ in range(reader.compact_size())]
        if has_witness:
            inputs = [replace(i, witness=tuple(reader.var_bytes() for _ in range(reader.compact_size())))
                      for i in inputs]
            if not any(i.witness for i in inputs):
                raise ValueError("superfluous witness record")
        locktime = reader.u32()
        if not reader.at_end():
            raise ValueError("bytes left over after the transaction")
        return cls(tuple(inputs), tuple(outputs), version, locktime)


class _Reader:
    def __init__(self, data: bytes) -> None:
        self._data, self._at = data, 0

    def take(self, n: int) -> bytes:
        if self._at + n > len(self._data):
            raise ValueError("end of data")
        chunk = self._data[self._at: self._at + n]
        self._at += n
        return chunk

    def byte(self) -> int:
        return self.take(1)[0]

    def u32(self) -> int:
        return struct.unpack("<I", self.take(4))[0]

    def i64(self) -> int:
        return struct.unpack("<q", self.take(8))[0]

    def compact_size(self) -> int:
        first = self.byte()
        if first < 0xFD:
            return first
        size, minimum = {0xFD: (2, 0xFD), 0xFE: (4, 0x1_0000), 0xFF: (8, 0x1_0000_0000)}[first]
        value = int.from_bytes(self.take(size), "little")
        if value < minimum:
            raise ValueError("non-canonical CompactSize")
        return value

    def var_bytes(self) -> bytes:
        return self.take(self.compact_size())

    def at_end(self) -> bool:
        return self._at == len(self._data)
