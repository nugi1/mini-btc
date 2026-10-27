"""
Shared fixtures. Tests marked `integration` talk to the docker compose services; they fail with a
clear message, rather than skip, when those are down, the same way the .NET integration tests do.
Run only the unit tests with `pytest -m "not integration"`.
"""

from __future__ import annotations

import json
import os
from pathlib import Path

import pytest

from btc_rpc import CoreRpc, CoreRpcError
from tests.fakes import FakeHttp

REPO_ROOT = Path(__file__).resolve().parents[2]
REGTEST_FIXTURES = REPO_ROOT / "tests" / "MiniBtc.Domain.Tests" / "TestData" / "regtest"


def load_fixture(name: str) -> dict:
    """A block captured from our regtest Core by scripts/capture-fixtures.sh (getblock verbosity 2)."""
    return json.loads((REGTEST_FIXTURES / f"{name}.json").read_text())


@pytest.fixture(scope="session")
def core() -> CoreRpc:
    rpc = CoreRpc()
    try:
        chain = rpc.getblockchaininfo()["chain"]
    except CoreRpcError as error:
        pytest.fail(f"Bitcoin Core is not reachable ({error}). Start it with `docker compose up -d --wait`.")
    if chain != "regtest":
        pytest.fail(f"Core follows '{chain}', these tests need regtest")
    return rpc


@pytest.fixture(scope="session")
def node_url() -> str:
    return os.environ.get("MINIBTC_NODE_URL", "http://127.0.0.1:5080")


@pytest.fixture
def fake_http():
    """Starts FakeHttp servers for a test and stops them afterwards."""
    servers: list[FakeHttp] = []

    def start(route) -> FakeHttp:
        server = FakeHttp(route)
        servers.append(server)
        return server

    yield start
    for server in servers:
        server.close()
