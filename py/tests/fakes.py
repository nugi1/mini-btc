"""A real HTTP server on a free local port with canned replies, standing in for Core or the node."""

from __future__ import annotations

import json
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from typing import Callable

# (method, path, headers, parsed JSON body or None) -> (status, reply body)
Route = Callable[[str, str, dict, object], tuple[int, bytes | dict]]


class FakeHttp:
    def __init__(self, route: Route) -> None:
        self.requests: list[tuple[str, str, dict, object]] = []
        fake = self

        class Handler(BaseHTTPRequestHandler):
            def _serve(self) -> None:
                length = int(self.headers.get("Content-Length") or 0)
                body = json.loads(self.rfile.read(length)) if length else None
                request = (self.command, self.path, dict(self.headers), body)
                fake.requests.append(request)
                status, reply = route(*request)
                payload = reply if isinstance(reply, bytes) else json.dumps(reply).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)

            do_GET = do_POST = _serve

            def log_message(self, *args) -> None:
                pass

        self._server = HTTPServer(("127.0.0.1", 0), Handler)
        threading.Thread(target=self._server.serve_forever, daemon=True).start()
        self.url = f"http://127.0.0.1:{self._server.server_port}/"

    def close(self) -> None:
        self._server.shutdown()
        self._server.server_close()


def fixed(status: int, reply: bytes | dict) -> Route:
    return lambda *_: (status, reply)
