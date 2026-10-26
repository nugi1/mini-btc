#!/usr/bin/env bash
# End-to-end smoke test of the running node against the compose services:
#   1. starts the node and waits until it has caught up with Core,
#   2. mines one block in Core,
#   3. checks that our node validated and stored it (tip height AND hash equal Core's), that the
#      ZMQ announcement arrived, and that BlockConnectedV1 came back from RabbitMQ.
# Usage: scripts/smoke-test.sh            (node on port 5099, so it doesn't clash with a running one)
set -euo pipefail
cd "$(dirname "$0")/.."

port="${PORT:-5099}"
address="mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r"   # regtest P2PKH of private key 1
log="$(mktemp -t minibtc-node.XXXXXX)"

our_height() { docker compose exec -T postgres psql -U minibtc -d minibtc -tAc "select coalesce(max(tip_height), -1) from chain_state"; }
our_tip()    { docker compose exec -T postgres psql -U minibtc -d minibtc -tAc "select encode(tip_hash, 'hex') from chain_state"; }
core_height() { scripts/btc-cli.sh getblockcount; }
# The database stores hashes in wire order; Core shows them reversed.
reverse_bytes() { echo "$1" | fold -w2 | { tac 2>/dev/null || tail -r; } | tr -d '\n'; }

fail() {
  echo "FAIL: $1"
  echo "---- last 30 lines of the node log ($log)"
  tail -n 30 "$log"
  exit 1
}

wait_until_level() { # <tries of 0.1 s> — prints how many tries it took
  local tries=0
  while [ "$tries" -lt "$1" ]; do
    if [ "$(our_height)" = "$(core_height)" ]; then echo "$tries"; return 0; fi
    sleep 0.1
    tries=$((tries + 1))
  done
  return 1
}

echo "== services"
docker compose up -d --wait >/dev/null

echo "== starting the node on port $port (log: $log)"
dotnet build src/MiniBtc.Api -v quiet -nologo >/dev/null
dotnet run --project src/MiniBtc.Api --no-build --urls "http://127.0.0.1:$port" >"$log" 2>&1 &
node_pid=$!
trap 'kill "$node_pid" 2>/dev/null || true; wait "$node_pid" 2>/dev/null || true' EXIT

for _ in $(seq 1 60); do
  curl -fs "http://127.0.0.1:$port/health" >/dev/null && break
  sleep 1
done
curl -fs "http://127.0.0.1:$port/health" >/dev/null || fail "the node did not answer /health within 60 s"

echo "== catching up with Core (height $(core_height))"
wait_until_level 600 >/dev/null || fail "our tip ($(our_height)) did not reach Core's ($(core_height)) within 60 s"
echo "   level at height $(our_height)"

echo "== mining one block"
hash="$(scripts/btc-cli.sh generatetoaddress 1 "$address" | tr -d ' "[]\n')"
height="$(core_height)"
echo "   Core mined $hash at height $height"

tries="$(wait_until_level 100)" || fail "our node did not store block $height within 10 s"
echo "   our node stored it after ~$((tries * 100)) ms"

[ "$(reverse_bytes "$(our_tip)")" = "$hash" ] || fail "our tip hash differs from Core's"
echo "   our tip hash equals Core's"

grep -q "Core announced block $hash" "$log" || fail "no ZMQ announcement for $hash in the log (did the 5 s poll do the work?)"
echo "   ZMQ announcement arrived through RabbitMQ"

for _ in $(seq 1 50); do
  grep -q "BlockConnectedV1: height $height," "$log" && break
  sleep 0.1
done
grep -q "BlockConnectedV1: height $height," "$log" || fail "BlockConnectedV1 for height $height never came back from RabbitMQ"
echo "   BlockConnectedV1 for height $height came back from RabbitMQ"

echo "PASS"
