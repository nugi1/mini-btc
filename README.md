# mini-bitcoin

A .NET node that follows a Bitcoin Core **regtest** network and re-validates every block
and transaction with its own code, plus a Python wallet and miner that move real regtest
coins through it.

- `src/MiniBtc.Domain` — wire format, hashing, consensus rules, UTXO set, mempool (no I/O)
- `src/MiniBtc.Application` — use cases: chain sync, broadcast, parity
- `src/MiniBtc.Infrastructure` — Postgres, Bitcoin Core RPC, ZMQ
- `src/MiniBtc.Api` — REST, SignalR, explorer
- `py/` — wallet and miner (coming in E0/E3)

Build: `dotnet build MiniBtc.slnx && dotnet test MiniBtc.slnx`

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
