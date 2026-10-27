"""
A minimal Bitcoin Core JSON-RPC 2.0 client on the standard library.

Core (since v28) answers a request that says "jsonrpc": "2.0" with HTTP 200 for both results and
errors; the error is a {code, message} object in the body. Wrong credentials are the exception:
the HTTP layer refuses them with 401 and an empty body, before any JSON is read.
"""

from __future__ import annotations

import base64
import itertools
import json
import os
import urllib.error
import urllib.request
from decimal import Decimal
from typing import Any


class RpcErrorCode:
    """The Core error codes the tools react to (src/rpc/protocol.h)."""

    MISC_ERROR = -1
    TYPE_ERROR = -3
    INVALID_ADDRESS_OR_KEY = -5
    INVALID_PARAMETER = -8
    VERIFY_ERROR = -25
    VERIFY_REJECTED = -26
    VERIFY_ALREADY_IN_CHAIN = -27
    IN_WARMUP = -28


class CoreRpcError(Exception):
    """Core answered with an error object, or could not be reached at all (code is None)."""

    def __init__(self, method: str, code: int | None, message: str) -> None:
        super().__init__(f"{method}: {message}" if code is None else f"{method}: {message} (code {code})")
        self.method = method
        self.code = code
        self.message = message


class CoreRpc:
    """
    Calls Core's RPC. Amounts in results are parsed as Decimal, never float, so 0.1 BTC stays
    exactly 10,000,000 satoshis. Any RPC can be called by name: ``rpc.getblockcount()``.
    """

    def __init__(
        self,
        url: str | None = None,
        user: str | None = None,
        password: str | None = None,
        timeout: float = 30.0,
    ) -> None:
        self.url = url or os.environ.get("MINIBTC_CORE_RPC_URL", "http://127.0.0.1:18443/")
        user = user or os.environ.get("MINIBTC_CORE_RPC_USER", "minibtc")
        password = password or os.environ.get("MINIBTC_CORE_RPC_PASSWORD", "minibtc-regtest")
        token = base64.b64encode(f"{user}:{password}".encode()).decode("ascii")
        self._authorization = f"Basic {token}"
        self._timeout = timeout
        self._ids = itertools.count(1)

    def call(self, method: str, *params: Any) -> Any:
        body = json.dumps(
            {"jsonrpc": "2.0", "id": next(self._ids), "method": method, "params": list(params)},
            default=_encode_decimal,
        ).encode()
        request = urllib.request.Request(
            self.url,
            data=body,
            method="POST",
            headers={"Content-Type": "application/json", "Authorization": self._authorization},
        )
        try:
            with urllib.request.urlopen(request, timeout=self._timeout) as response:
                payload = response.read()
        except urllib.error.HTTPError as error:
            # Core 2.0 replies use 200 even for errors, but older-style replies (and proxies) may not.
            payload = error.read()
            if error.code == 401:
                raise CoreRpcError(method, None, "401 Unauthorized: check the RPC user and password") from error
            if not payload:
                raise CoreRpcError(method, None, f"HTTP {error.code} {error.reason}") from error
        except (urllib.error.URLError, OSError) as error:
            raise CoreRpcError(method, None, f"cannot reach Core at {self.url}: {error}") from error

        reply = json.loads(payload, parse_float=Decimal)
        if reply.get("error"):
            raise CoreRpcError(method, reply["error"].get("code"), reply["error"].get("message", ""))
        return reply.get("result")

    def __getattr__(self, method: str):
        if method.startswith("_"):
            raise AttributeError(method)
        return lambda *params: self.call(method, *params)


def _encode_decimal(value: Any) -> Any:
    if isinstance(value, Decimal):
        # Core's AmountFromValue takes a JSON number or a string; a string carries the exact digits.
        return str(value)
    raise TypeError(f"{type(value).__name__} is not JSON serializable")
