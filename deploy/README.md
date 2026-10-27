# Deploying the public demo

One small Linux server runs everything: Core (regtest), PostgreSQL, RabbitMQ, the node, the demo
driver and Caddy (HTTPS). Only ports 80 and 443 are open to the internet.

## 1. A server

Any Ubuntu 24.04 VM with 2 vCPU, 4 GB RAM and 40 GB disk is enough for regtest (for example a small
Hetzner or DigitalOcean instance). Open ports 22, 80 and 443 in its firewall.

## 2. Docker

```bash
ssh root@<server-ip>
curl -fsSL https://get.docker.com | sh
```

## 3. The code

From your Mac:

```bash
rsync -av --exclude bin --exclude obj --exclude .venv --exclude .git ~/RiderProjects/mini-bitcoin root@<server-ip>:/opt/
```

## 4. Settings

On the server, create `/opt/mini-bitcoin/.env`. With no domain of your own, sslip.io gives every IP
a name that Let's Encrypt can certify:

```bash
cd /opt/mini-bitcoin
cat > .env <<'ENV'
SITE_ADDRESS=<server-ip>.sslip.io
BTC_RPC_PASSWORD=<a long random string>
POSTGRES_PASSWORD=<a long random string>
RABBITMQ_PASSWORD=<a long random string>
ENV
```

## 5. Start

```bash
docker compose -f docker-compose.yml -f docker-compose.deploy.yml up -d --build
docker compose -f docker-compose.yml -f docker-compose.deploy.yml logs -f node demo
```

Open `https://<server-ip>.sslip.io`. The demo driver funds itself, then sends a payment every ~20 s
and mines a block every ~60 s, so the explorer is live the moment someone opens it.

## 6. Let visitors send coins

Each visitor opens **Wallet** in the sidebar (`https://<server-ip>.sslip.io/#/wallet`), creates a
wallet (the key stays in their browser), and presses **Get 5 test BTC**. The faucet in the demo
service pays them from the miner's coins and mines a block, so the coins are spendable at once. The
Receive QR code is a link to the payer's wallet page with the address filled in: scan it with
another phone's camera, enter an amount, review, broadcast. Everyone's explorer shows the payment
arrive in the mempool and confirm with the next block.

The faucet is rate-limited (one payment per address per 30 s, six per visitor IP per 10 minutes) and
only reachable through Caddy's `/faucet` route; the node itself still holds no keys.

Update after changes: rsync again, then rerun the `up -d --build` command. Start over with a fresh
chain: `docker compose -f docker-compose.yml -f docker-compose.deploy.yml down -v`.
