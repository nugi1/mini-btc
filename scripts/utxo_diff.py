"""
Finds where our node's UTXO set and Bitcoin Core's differ, coin by coin.

It rebuilds the UTXO set a third way, from Core's own decoded blocks (getblock verbosity 2): every
output that isn't provably unspendable is added, every input removes the coin it spends, genesis is
skipped. It compares that with Core's gettxoutsetinfo and with the node's utxos table.

    cd py && uv run python ../scripts/utxo_diff.py
"""

import subprocess
import sys
from decimal import Decimal
from pathlib import Path

from btc_rpc import CoreRpc

rpc = CoreRpc()
tip = rpc.getblockcount()
rebuilt: dict[tuple[str, int], tuple[int, int, str]] = {}  # (txid, vout) -> (sats, height, script)
for height in range(1, tip + 1):
    block = rpc.getblock(rpc.getblockhash(height), 2)
    for tx in block["tx"]:
        for vin in tx["vin"]:
            if "coinbase" not in vin:
                rebuilt.pop((vin["txid"], vin["vout"]), None)
        for vout in tx["vout"]:
            script = vout["scriptPubKey"]["hex"]
            if script.startswith("6a") or len(script) > 20000:
                continue  # OP_RETURN or over 10,000 bytes: never a coin
            rebuilt[(tx["txid"], vout["n"])] = (int(vout["value"] * 100_000_000), height, script)

info = rpc.gettxoutsetinfo()
print(f"Core tip {tip}: gettxoutsetinfo txouts={info['txouts']} total={info['total_amount']}")
print(f"rebuilt from Core's blocks: {len(rebuilt)} coins, total={Decimal(sum(v[0] for v in rebuilt.values())) / 100_000_000}")

rows = subprocess.run(
    ["docker", "compose", "exec", "-T", "postgres", "psql", "-U", "minibtc", "-d", sys.argv[1] if len(sys.argv) > 1 else "minibtc",
     "-At", "-F", " ", "-c", "select encode(txid, 'hex'), vout, value, height from utxos"],
    capture_output=True, text=True, check=True, cwd=Path(__file__).resolve().parents[1]).stdout.split()
ours = {}
for i in range(0, len(rows), 4):
    txid = bytes.fromhex(rows[i])[::-1].hex()  # stored in wire order
    ours[(txid, int(rows[i + 1]))] = (int(rows[i + 2]), int(rows[i + 3]))
print(f"node's utxos table: {len(ours)} coins")

for key in sorted(rebuilt.keys() - ours.keys(), key=lambda k: rebuilt[k][1]):
    sats, height, script = rebuilt[key]
    print(f"  missing in node: {key[0]}:{key[1]} {sats} sat, height {height}, script {script}")
for key in sorted(ours.keys() - rebuilt.keys(), key=lambda k: ours[k][1]):
    print(f"  extra in node:   {key[0]}:{key[1]} {ours[key][0]} sat, height {ours[key][1]}")
