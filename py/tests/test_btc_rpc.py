"""The RPC client against a fake Core: what it sends, and how it reads replies and errors."""

from __future__ import annotations

import base64
from decimal import Decimal

import pytest

from btc_rpc import CoreRpc, CoreRpcError, RpcErrorCode
from tests.fakes import fixed


def test_sends_a_json_rpc_2_request_with_basic_auth(fake_http):
    fake = fake_http(fixed(200, {"jsonrpc": "2.0", "id": 1, "result": 107}))
    rpc = CoreRpc(fake.url, "minibtc", "minibtc-regtest")

    assert rpc.getblockcount() == 107

    _, _, headers, body = fake.requests[0]
    assert body == {"jsonrpc": "2.0", "id": 1, "method": "getblockcount", "params": []}
    assert headers["Authorization"] == "Basic " + base64.b64encode(b"minibtc:minibtc-regtest").decode()


def test_amounts_are_read_as_exact_decimals(fake_http):
    fake = fake_http(fixed(200, b'{"jsonrpc":"2.0","id":1,"result":{"amount":0.1}}'))

    assert CoreRpc(fake.url).call("x")["amount"] == Decimal("0.1")


def test_decimal_params_are_sent_as_exact_strings(fake_http):
    fake = fake_http(fixed(200, {"jsonrpc": "2.0", "id": 1, "result": None}))

    CoreRpc(fake.url).call("sendtoaddress", "addr", Decimal("0.1"))

    assert fake.requests[0][3]["params"] == ["addr", "0.1"]


def test_an_error_object_in_a_200_reply_raises_with_cores_code(fake_http):
    fake = fake_http(fixed(200, {"jsonrpc": "2.0", "id": 1, "error": {"code": -5, "message": "Invalid address"}}))

    with pytest.raises(CoreRpcError) as raised:
        CoreRpc(fake.url).generatetoaddress(1, "nope")

    assert (raised.value.method, raised.value.code, raised.value.message) == (
        "generatetoaddress", RpcErrorCode.INVALID_ADDRESS_OR_KEY, "Invalid address")


def test_401_says_the_credentials_are_wrong(fake_http):
    fake = fake_http(fixed(401, b""))

    with pytest.raises(CoreRpcError, match="401 Unauthorized"):
        CoreRpc(fake.url, "minibtc", "wrong").getblockcount()


def test_an_unreachable_core_is_an_error_without_a_code():
    with pytest.raises(CoreRpcError) as raised:
        CoreRpc("http://127.0.0.1:9/").getblockcount()

    assert raised.value.code is None


@pytest.mark.integration
def test_the_compose_core_is_regtest_at_our_genesis(core):
    assert core.getblockhash(0) == "0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206"
