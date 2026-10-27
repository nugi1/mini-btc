# mini-bitcoin Python tools

| Package | What it owns | Talks to |
| --- | --- | --- |
| `btc_rpc` | A small JSON-RPC client for Bitcoin Core | Core's RPC (127.0.0.1:18443) |
| `wallet` | Keys, addresses, coin selection, signing | The node's REST API for UTXOs; Core to broadcast (until E4's gateway) |
| `miner` | Producing regtest blocks | Core's RPC |

The wallet never decides validity and the node never sees a private key.

```bash
cd py
uv sync
uv run pytest -m "not integration"   # unit tests, no services needed
uv run pytest                        # also against docker compose (and the node, for the wallet test)
uv run python -m miner generate 101 --to <address>
```

Connection settings come from the environment, with defaults matching `docker-compose.yml`:
`MINIBTC_CORE_RPC_URL` (`http://127.0.0.1:18443/`), `MINIBTC_CORE_RPC_USER` (`minibtc`),
`MINIBTC_CORE_RPC_PASSWORD` (`minibtc-regtest`) and `MINIBTC_NODE_URL` (`http://127.0.0.1:5080`).
