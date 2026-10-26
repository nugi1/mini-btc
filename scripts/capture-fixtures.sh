#!/usr/bin/env bash
# Captures real blocks from the regtest node in docker compose as test fixtures for
# tests/MiniBtc.Domain.Tests (TestData/regtest/<name>.hex = getblock 0, <name>.json = getblock 2):
#
#   genesis        height 0
#   coinbase-only  just a coinbase, which still carries the SegWit witness commitment
#   segwit-spend   coinbase + one tx spending a P2WPKH coin to a P2PKH and a P2WPKH output
#   mixed          coinbase + a legacy P2PKH spend (no witness) + a SegWit spend:
#                  three txids, so the merkle tree pairs the last one with itself
#
# A throwaway Core wallet named "fixtures" creates the transactions. The node never uses
# Core's wallet; scripts/reset.sh removes it with everything else.
set -euo pipefail
cd "$(dirname "$0")/.."

cli() { scripts/btc-cli.sh "$@"; }
wallet() { scripts/btc-cli.sh -rpcwallet=fixtures "$@"; }
# First value of a JSON field, e.g. `field txid`; enough for Core's flat, pretty-printed output.
field() { sed -n "s/.*\"$1\": \"\{0,1\}\([^\",]*\)\"\{0,1\}.*/\1/p" | head -n 1; }

out=tests/MiniBtc.Domain.Tests/TestData/regtest
mkdir -p "$out"

capture() { # <name> <block hash>
  cli getblock "$2" 0 | tr -d '\r\n' > "$out/$1.hex"
  cli getblock "$2" 2 > "$out/$1.json"
  echo "captured $1: $2"
}

cli loadwallet fixtures > /dev/null 2>&1 || cli createwallet fixtures > /dev/null 2>&1 || true
miner=$(wallet getnewaddress "" bech32)

capture genesis "$(cli getblockhash 0)"

# 101 blocks to a P2WPKH address: the first of them matures (COINBASE_MATURITY = 100).
cli generatetoaddress 101 "$miner" > /dev/null
capture coinbase-only "$(cli getbestblockhash)"

legacy=$(wallet getnewaddress "" legacy)
segwit=$(wallet getnewaddress "" bech32)
wallet -named sendmany amounts="{\"$legacy\":1,\"$segwit\":2}" fee_rate=10 > /dev/null
cli generatetoaddress 1 "$miner" > /dev/null
capture segwit-spend "$(cli getbestblockhash)"

# Spend exactly the P2PKH coin, so this tx has a scriptSig signature and no witness.
coin=$(wallet listunspent 1 9999999 "[\"$legacy\"]")
txid=$(echo "$coin" | field txid)
vout=$(echo "$coin" | field vout)
raw=$(wallet createrawtransaction "[{\"txid\":\"$txid\",\"vout\":$vout}]" "[{\"$segwit\":0.9999}]")
cli sendrawtransaction "$(wallet signrawtransactionwithwallet "$raw" | field hex)" > /dev/null
wallet -named sendtoaddress address="$segwit" amount=0.5 fee_rate=10 > /dev/null
cli generatetoaddress 1 "$miner" > /dev/null
capture mixed "$(cli getbestblockhash)"
