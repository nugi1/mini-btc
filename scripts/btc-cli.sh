#!/usr/bin/env bash
# bitcoin-cli against the regtest node in docker compose.
# Usage: scripts/btc-cli.sh getblockchaininfo
#        scripts/btc-cli.sh createwallet dev && scripts/btc-cli.sh -rpcwallet=dev getnewaddress
set -euo pipefail
cd "$(dirname "$0")/.."
exec docker compose exec -T bitcoind sh -c \
  'bitcoin-cli -regtest -rpcuser="$BTC_RPC_USER" -rpcpassword="$BTC_RPC_PASSWORD" "$@"' bitcoin-cli "$@"
