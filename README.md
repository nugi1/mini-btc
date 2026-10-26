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
