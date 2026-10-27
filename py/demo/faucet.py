"""
The faucet's HTTP side: POST /faucet {"address": ...}, POST /faucet/mine, GET /faucet/health.

It lives in the demo service, which already holds keys, and not in the node: the node never holds
keys, even in the demo. Caddy routes /faucet/* here on the public server. The handlers that move
coins are passed in by demo.__main__; this module only parses requests and rate-limits them.
"""

from __future__ import annotations

import json
import threading
import time
from collections import defaultdict, deque
from collections.abc import Callable
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class FaucetError(Exception):
    """A refusal the visitor should see: the message goes into the JSON reply."""

    def __init__(self, status: int, message: str) -> None:
        super().__init__(message)
        self.status = status


class RateLimit:
    """At most `count` events per `seconds` for each key (an address, a client IP, or "*" for everyone)."""

    def __init__(self, count: int, seconds: float) -> None:
        self.count, self.seconds = count, seconds
        self._events: dict[str, deque[float]] = defaultdict(deque)
        self._lock = threading.Lock()

    def check(self, key: str, now: float | None = None) -> float:
        """0 if allowed (and recorded), else the seconds until the next event is allowed."""
        now = time.monotonic() if now is None else now
        with self._lock:
            events = self._events[key]
            while events and now - events[0] >= self.seconds:
                events.popleft()
            if len(events) >= self.count:
                return self.seconds - (now - events[0])
            events.append(now)
            return 0.0


def limited(*checks: tuple[RateLimit, str]) -> None:
    for limit, key in checks:
        wait = limit.check(key)
        if wait:
            raise FaucetError(429, f"Slow down: try again in {int(wait) + 1} s.")


def serve(port: int, fund: Callable[[str], dict], mine: Callable[[], dict], log: Callable[[str], None]) -> ThreadingHTTPServer:
    per_address, per_client, everyone = RateLimit(1, 30), RateLimit(6, 600), RateLimit(40, 600)
    mine_per_client, mine_everyone = RateLimit(1, 10), RateLimit(1, 3)

    class Handler(BaseHTTPRequestHandler):
        server_version = "minibtc-faucet"

        def do_GET(self) -> None:  # noqa: N802 (http.server's naming)
            if self.path.rstrip("/") == "/faucet/health":
                return self._reply(200, {"status": "ok"})
            self._reply(404, {"error": "not found"})

        def do_POST(self) -> None:  # noqa: N802
            path = self.path.rstrip("/")
            try:
                if path == "/faucet":
                    address = str(self._json().get("address", "")).strip()
                    if not address:
                        raise FaucetError(400, "address is required")
                    limited((per_address, address), (per_client, self._client()), (everyone, "*"))
                    return self._reply(200, fund(address))
                if path == "/faucet/mine":
                    limited((mine_per_client, self._client()), (mine_everyone, "*"))
                    return self._reply(200, mine())
                self._reply(404, {"error": "not found"})
            except FaucetError as error:
                self._reply(error.status, {"error": str(error)})
            except Exception as error:  # the demo must keep serving; the log has the detail
                log(f"faucet error: {error!r}")
                self._reply(503, {"error": "The faucet is busy or the chain is unreachable; try again shortly."})

        def _json(self) -> dict:
            length = int(self.headers.get("Content-Length") or 0)
            if length > 1024:
                raise FaucetError(413, "request too large")
            try:
                body = json.loads(self.rfile.read(length) or b"{}")
            except ValueError:
                raise FaucetError(400, "the body must be JSON") from None
            if not isinstance(body, dict):
                raise FaucetError(400, "the body must be a JSON object")
            return body

        def _client(self) -> str:
            # Caddy appends the visitor's address to X-Forwarded-For; the last entry is the one it saw.
            forwarded = self.headers.get("X-Forwarded-For", "")
            return forwarded.split(",")[-1].strip() or self.client_address[0]

        def _reply(self, status: int, body: dict) -> None:
            data = json.dumps(body).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(data)

        def log_message(self, format: str, *args) -> None:  # noqa: A002
            log(f"faucet {self.address_string()} {format % args}")

    server = ThreadingHTTPServer(("0.0.0.0", port), Handler)
    server.daemon_threads = True
    threading.Thread(target=server.serve_forever, name="faucet", daemon=True).start()
    return server
