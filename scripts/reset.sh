#!/usr/bin/env bash
# Throws away the regtest chain, the database and the queues, then starts fresh.
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose down --volumes
docker compose up -d --wait
