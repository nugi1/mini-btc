# mini-bitcoin

A .NET node that follows a Bitcoin Core **regtest** network and re-validates every block
and transaction with its own code, plus a Python wallet and miner that move real regtest
coins through it.

- `src/MiniBtc.Domain` — wire format, hashing, consensus rules, UTXO set, mempool (no I/O)
- `src/MiniBtc.Application` — use cases: chain sync, broadcast, parity
- `src/MiniBtc.Infrastructure` — Postgres, Bitcoin Core RPC, ZMQ
- `src/MiniBtc.Api` — REST, SignalR, explorer
- `py/` — Python wallet (keys, coin selection, signing) and miner; see `py/README.md`

Build: `dotnet build MiniBtc.slnx && dotnet test MiniBtc.slnx`

## Explorer

Start the node and open <http://127.0.0.1:5080>. It shows the chain as the node validated it: tip
and sync state against Core, latest blocks, the mempool view, transactions decoded input by input,
address balances, a parity check against Core, and a broadcast form for the gateway. Updates arrive
live over SignalR (`/hubs/chain`). It is plain HTML, CSS and JavaScript modules in
`src/MiniBtc.Api/wwwroot`: no build step.

JSON behind it: `GET /chain/summary`, `/blocks`, `/blocks/{height|hash}`, `/tx/{txid}`,
`/mempool/entries`, `/parity?address=…`.

Rejections are logged with the rule as a structured `{Rule}` field; for one JSON object per line,
run with `Logging__Console__FormatterName=json`.

## Local environment

```bash
cp .env.example .env            # optional: change credentials or ports
docker compose up -d --wait     # bitcoind (regtest), PostgreSQL, RabbitMQ
scripts/btc-cli.sh getblockchaininfo
```

| Service | Host address | Purpose |
| --- | --- | --- |
| bitcoind RPC | 127.0.0.1:18443 | Catch-up sync, broadcast, parity checks |
| ZMQ rawblock / rawtx / sequence | 127.0.0.1:28332 / 28333 / 28336 | Live block and tx notifications |
| PostgreSQL | 127.0.0.1:5433 | Blocks, UTXO set, undo data, outbox/inbox |
| RabbitMQ | 127.0.0.1:5672, UI :15672 | Transport for domain events via the outbox |

`scripts/reset.sh` wipes everything and starts a fresh chain.

## Wallet walkthrough

With the services and the node running (`dotnet run --project src/MiniBtc.Api --urls http://127.0.0.1:5080`):

```bash
cd py && uv sync
uv run python -m wallet --wallet alice new
uv run python -m wallet --wallet bob new
uv run python -m miner generate 101 --to "$(uv run python -m wallet --wallet alice address)"
uv run python -m wallet --wallet alice balance
uv run python -m wallet --wallet alice send --to "$(uv run python -m wallet --wallet bob address)" --amount 10 --dry-run
```

`send` without `--dry-run` goes through the node's gateway (`POST /tx`): the node checks the
transaction against its own UTXO set and mempool view, and only then hands it to Core.

The node answers `GET /addresses/{address}/utxos` and `/balance` (satoshis; spendable means
spendable in the next block), `POST /tx` and `GET /mempool`. Wallet keys live in
`~/.minibtc/<name>.json`, readable only by you.

### Double spend and reorg

```bash
uv run python -m wallet --wallet carol new
uv run python -m wallet --wallet alice send --to "$(uv run python -m wallet --wallet bob address)" --amount 10
# Spending the same coin again is refused by the node (bip125-replacement-disallowed) ...
uv run python -m wallet --wallet alice send --to "$(uv run python -m wallet --wallet carol address)" --amount 10
# ... and, sent straight to Core, by Core ("insufficient fee": a replacement must pay more).
uv run python -m wallet --wallet alice send --to "$(uv run python -m wallet --wallet carol address)" --amount 10 --direct

# Make Core drop its tip; the node disconnects the block from its undo data and follows.
scripts/btc-cli.sh invalidateblock "$(scripts/btc-cli.sh getbestblockhash)"
```
