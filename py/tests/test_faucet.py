"""The faucet's HTTP layer: request parsing, refusals as JSON, rate limits. No chain needed."""

from __future__ import annotations

import json
import urllib.error
import urllib.request

import pytest

from demo.faucet import FaucetError, RateLimit, serve


def test_a_rate_limit_allows_count_events_per_window_per_key():
    limit = RateLimit(2, 60)

    assert limit.check("a", now=0) == 0
    assert limit.check("a", now=1) == 0
    assert limit.check("a", now=2) == pytest.approx(58)  # the third waits for the first to age out
    assert limit.check("b", now=2) == 0  # keys are independent
    assert limit.check("a", now=60) == 0


@pytest.fixture
def faucet():
    paid: list[str] = []

    def fund(address: str) -> dict:
        if address == "bad":
            raise FaucetError(400, "Invalid address")
        paid.append(address)
        return {"txid": "ab" * 32, "amount": 500_000_000, "height": 7, "block": "cd" * 32}

    server = serve(0, fund, lambda: {"height": 8, "block": "ef" * 32}, lambda _: None)
    yield f"http://127.0.0.1:{server.server_address[1]}", paid
    server.shutdown()


def call(url: str, path: str, body: dict | None = None) -> tuple[int, dict]:
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(url + path, data=data, method="GET" if body is None else "POST",
                                     headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=5) as response:
            return response.status, json.loads(response.read())
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read())


def test_fund_pays_once_then_rate_limits_the_same_address(faucet):
    url, paid = faucet

    assert call(url, "/faucet/health") == (200, {"status": "ok"})
    status, reply = call(url, "/faucet", {"address": "mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r"})
    assert (status, reply["amount"], reply["height"]) == (200, 500_000_000, 7)

    status, reply = call(url, "/faucet", {"address": "mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r"})
    assert status == 429 and reply["error"].startswith("Slow down")
    assert paid == ["mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r"]


def test_refusals_come_back_as_json_errors(faucet):
    url, _ = faucet

    assert call(url, "/faucet", {"address": "bad"}) == (400, {"error": "Invalid address"})
    assert call(url, "/faucet", {}) == (400, {"error": "address is required"})
    assert call(url, "/faucet/nope", {}) == (404, {"error": "not found"})
    assert call(url, "/faucet/mine", {})[1]["height"] == 8
