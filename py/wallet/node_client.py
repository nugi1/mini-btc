"""The node's REST API, as the wallet uses it: which coins does an address have?"""

from __future__ import annotations

import json
import os
import urllib.error
import urllib.parse
import urllib.request

from wallet.builder import Coin
from wallet.tx import OutPoint


class NodeError(Exception):
    def __init__(self, status: int | None, message: str) -> None:
        super().__init__(message if status is None else f"{message} (HTTP {status})")
        self.status = status
        self.message = message


class NodeClient:
    def __init__(self, url: str | None = None, timeout: float = 10.0) -> None:
        self.url = (url or os.environ.get("MINIBTC_NODE_URL", "http://127.0.0.1:5080")).rstrip("/")
        self._timeout = timeout

    def utxos(self, address: str) -> tuple[int, list[Coin]]:
        """Our node's tip height and the address's unspent coins, each marked spendable or not."""
        reply = self._get(f"/addresses/{urllib.parse.quote(address, safe='')}/utxos")
        coins = [
            Coin(
                outpoint=OutPoint(u["txid"], u["vout"]),
                value=u["value"],
                script_pubkey=bytes.fromhex(reply["scriptPubKey"]),
                height=u["height"],
                coinbase=u["coinbase"],
                spendable=u["spendable"],
            )
            for u in reply["utxos"]
        ]
        return reply["tipHeight"], coins

    def balance(self, address: str) -> dict:
        return self._get(f"/addresses/{urllib.parse.quote(address, safe='')}/balance")

    def _get(self, path: str) -> dict:
        try:
            with urllib.request.urlopen(self.url + path, timeout=self._timeout) as response:
                return json.loads(response.read())
        except urllib.error.HTTPError as error:
            body = error.read()
            try:
                message = json.loads(body).get("error", body.decode(errors="replace"))
            except ValueError:
                message = body.decode(errors="replace") or error.reason
            raise NodeError(error.code, message) from error
        except (urllib.error.URLError, OSError) as error:
            raise NodeError(None, f"cannot reach the node at {self.url}: {error}") from error
