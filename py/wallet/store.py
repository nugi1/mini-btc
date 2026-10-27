"""
Where a wallet's key lives: a JSON file readable only by its owner. One key per wallet keeps the
demo legible (Alice, Bob and Carol are three files); HD wallets are out of scope.
"""

from __future__ import annotations

import json
import os
from pathlib import Path

from wallet.keys import Key
from wallet.network import REGTEST, Network


def wallet_path(name: str) -> Path:
    """A name like "alice" lives in $MINIBTC_WALLET_DIR (default ~/.minibtc); a path is used as is."""
    if os.sep in name or name.endswith(".json"):
        return Path(name).expanduser()
    directory = Path(os.environ.get("MINIBTC_WALLET_DIR", "~/.minibtc")).expanduser()
    return directory / f"{name}.json"


def create(path: Path, key: Key, network: Network = REGTEST) -> None:
    if path.exists():
        raise FileExistsError(f"{path} already exists; refusing to overwrite a key")
    path.parent.mkdir(parents=True, exist_ok=True)
    body = json.dumps({"network": network.name, "wif": key.wif(network), "address": key.address(network)}, indent=2)
    # Create with 0600 from the start, so the key is never readable by others, not even briefly.
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w") as file:
        file.write(body + "\n")


def load(path: Path, network: Network = REGTEST) -> Key:
    if not path.exists():
        raise FileNotFoundError(f"no wallet at {path}; create one with `python -m wallet --wallet <name> new`")
    data = json.loads(path.read_text())
    if data.get("network") != network.name:
        raise ValueError(f"{path} is a {data.get('network')} wallet, not {network.name}")
    key = Key.from_wif(data["wif"], network)
    if data.get("address") != key.address(network):
        raise ValueError(f"{path}: the stored address does not belong to the stored key")
    return key
