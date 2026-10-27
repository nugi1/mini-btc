// Architecture: the system map, the interface and event catalog, and a sequence diagram for every
// flow the application supports, drawn from data by one small SVG renderer (no library).
import { html, raw } from "../format.js";
import { pageHead } from "../ui.js";

const FLOWS = [
{
  id: "block-sync", num: "01", group: "FOLLOWING CORE", title: "Block sync",
  lead: "Core mines a block; the node fetches it as raw bytes, validates it with its own rules, and commits it with its UTXO changes in one database transaction. The event reaches browsers through the outbox, never before the commit.",
  meta: [["Trigger", "ZMQ rawblock / sequence C, a 5 s timer, startup"], ["Code", "ChainSyncWorker, ChainSynchronizer, UtxoSet.Connect"], ["Writes", "blocks, utxos, undo, outbox"]],
  parts: [["core", "Bitcoin Core", "regtest node", "cpp"], ["zmq", "ZMQ listeners", "BackgroundService", "cs"], ["mq", "RabbitMQ", "MassTransit bus", "infra"],
          ["sync", "Sync worker", "ChainSynchronizer", "cs"], ["dom", "Domain", "UtxoSet aggregate", "cs"], ["db", "PostgreSQL", "EF Core + outbox", "infra"], ["ui", "Browsers", "SignalR clients", "js"]],
  steps: [
    { m: ["core", "zmq", "ZMQ rawblock / sequence C"], async: true },
    { m: ["zmq", "mq", "publish BlockAnnounced"], async: true },
    { m: ["mq", "sync", "consume: wake the worker"], async: true },
    { note: ["sync", "sync"], text: "Idempotent: always walks from our tip to Core's tip, whatever woke it" },
    { m: ["sync", "core", "getblockchaininfo, getblockhash(h + 1)"] },
    { m: ["sync", "core", "getblock(hash, 0)"] },
    { m: ["core", "sync", "raw block bytes"], reply: true },
    { m: ["sync", "dom", "Block.Parse, then UtxoSet.Connect"] },
    { self: "dom", lines: ["Header: proof of work, builds on tip, merkle root", "Structure: size and weight, one leading coinbase", "Each tx: CheckTransaction rules", "Inputs: exist, unspent, mature, value balance", "Scripts: P2PKH signatures (ECDSA)", "Coinbase pays at most subsidy + fees"] },
    { alt: "a rule fails" },
    { note: ["sync", "dom"], text: "Stop with Core's reject reason and log the rule. The block is never skipped.", tone: "bad" },
    { alt: "else · block valid" },
    { m: ["dom", "sync", "ConnectedBlock, undo, BlockConnected"], reply: true },
    { m: ["sync", "db", "one transaction: block, UTXO changes, undo, outbox row"] },
    { self: "sync", lines: ["Mempool view: drop the block's txs"] },
    { m: ["db", "mq", "outbox delivery: BlockConnectedV1"], async: true },
    { m: ["mq", "ui", "hub relay: SignalR BlockConnected"], async: true },
  ],
  notes: ["The commit and the outbox row are one transaction, so a browser never hears about a block that is not stored.", "Reject codes mirror Core's reject reasons, e.g. bad-txnmrklroot, bad-txns-inputs-missingorspent."],
},
{
  id: "reorg", num: "02", group: "FOLLOWING CORE", title: "Reorg",
  lead: "When Core switches to another branch (a longer chain or invalidateblock), the node disconnects its own blocks newest first, restoring spent coins from undo data, until its tip is on Core's active chain. Then block sync connects Core's branch.",
  meta: [["Trigger", "Our tip no longer in Core's active chain"], ["Code", "ChainSynchronizer, UtxoSet.Disconnect"], ["Writes", "utxos restored, block removed, outbox"]],
  parts: [["core", "Bitcoin Core", "regtest node", "cpp"], ["sync", "Sync worker", "ChainSynchronizer", "cs"], ["dom", "Domain", "UtxoSet aggregate", "cs"],
          ["db", "PostgreSQL", "EF Core + outbox", "infra"], ["mq", "RabbitMQ", "MassTransit bus", "infra"], ["ui", "Browsers", "SignalR clients", "js"]],
  steps: [
    { note: ["core", "core"], text: "Core changes branch; ZMQ sequence D / C wakes the worker" },
    { m: ["sync", "core", "getblockhash(our tip height)"] },
    { m: ["core", "sync", "a different hash: our tip left Core's chain"], reply: true },
    { alt: "loop · until our tip is on Core's chain" },
    { m: ["sync", "db", "load the tip block and its undo data"] },
    { m: ["sync", "dom", "UtxoSet.Disconnect(block, undo)"] },
    { self: "dom", lines: ["Remove the outputs the block created", "Restore the coins it spent, from undo", "Tip moves back one block"] },
    { m: ["sync", "db", "commit + outbox row BlockDisconnectedV1"] },
    { self: "sync", lines: ["Mempool view: return the block's txs"] },
    { m: ["db", "mq", "BlockDisconnectedV1"], async: true },
    { m: ["mq", "ui", "SignalR BlockDisconnected"], async: true },
    { alt: "then" },
    { m: ["sync", "core", "connect Core's branch, block by block (flow 01)"] },
  ],
  notes: ["Each disconnected block is its own database transaction, so a crash mid-reorg leaves a consistent chain.", "If Core reorganizes again while we connect, the loop looks again before reporting itself level."],
},
{
  id: "mempool", num: "03", group: "FOLLOWING CORE", title: "Mempool mirror",
  lead: "The node keeps an in-memory view of Core's mempool: a full reload at startup, then Core's ZMQ sequence events. Core's mempool sequence number orders every event against the reload, so nothing is applied twice.",
  meta: [["Trigger", "Startup, then ZMQ sequence A and R"], ["Code", "MempoolSynchronizer, MempoolState"], ["Writes", "memory only"]],
  parts: [["core", "Bitcoin Core", "regtest node", "cpp"], ["zmq", "ZMQ listener", "sequence topic", "cs"], ["mq", "RabbitMQ", "MassTransit bus", "infra"],
          ["msync", "Mempool sync", "one message at a time", "cs"], ["view", "Mempool view", "MempoolState, in memory", "cs"], ["ui", "Browsers", "SignalR clients", "js"]],
  steps: [
    { alt: "at startup, after the first block sync" },
    { m: ["msync", "core", "getrawmempool(false, true)"] },
    { m: ["core", "msync", "txids + mempool_sequence"], reply: true },
    { m: ["msync", "core", "getrawtransaction per txid"] },
    { m: ["msync", "view", "Reload(txs, sequence)"] },
    { alt: "Core accepts a transaction" },
    { m: ["core", "zmq", "sequence: txid, A, mempool seq"], async: true },
    { m: ["zmq", "mq", "MempoolTxAdded"], async: true },
    { m: ["mq", "msync", "consume"], async: true },
    { note: ["msync", "msync"], text: "Skipped if seq is not above the view's" },
    { m: ["msync", "core", "getrawtransaction(txid)"] },
    { m: ["msync", "view", "Apply(seq): add"] },
    { m: ["msync", "ui", "SignalR TxAccepted (source: core)"], async: true },
    { alt: "Core drops a transaction: mined, replaced, expired" },
    { m: ["core", "zmq", "sequence: txid, R, mempool seq"], async: true },
    { m: ["zmq", "mq", "MempoolTxRemoved"], async: true },
    { m: ["mq", "msync", "consume"], async: true },
    { m: ["msync", "view", "Apply(seq): remove"] },
    { m: ["msync", "ui", "SignalR TxRemoved"], async: true },
  ],
  notes: ["Block sync also updates the view directly: confirmed txs leave it, a disconnected block's txs return.", "The view is what the gateway (04) checks double spends against."],
},
{
  id: "broadcast", num: "04", group: "TRANSACTIONS", title: "Broadcast gateway",
  lead: "Every wallet sends signed transactions here. The node judges each one with its own rules against its UTXO set and mempool view; only what it accepts goes on to Core, which judges it again. A rejection says who refused it.",
  meta: [["Endpoint", "POST /tx { hex }"], ["Code", "TransactionBroadcaster, Mempool.Check"], ["Result", "200 txid · 400 rejectedBy node | core"]],
  parts: [["w", "Wallet", "browser, CLI or demo", "js"], ["api", "POST /tx", "TransactionEndpoints", "cs"], ["gw", "Gateway", "TransactionBroadcaster", "cs"],
          ["db", "PostgreSQL", "UTXO set", "infra"], ["dom", "Mempool.Check", "domain rules", "cs"], ["core", "Bitcoin Core", "regtest node", "cpp"], ["ui", "Browsers", "SignalR clients", "js"]],
  steps: [
    { m: ["w", "api", "POST /tx { hex }"] },
    { m: ["api", "gw", "BroadcastAsync(hex)"] },
    { self: "gw", lines: ["Transaction.Parse (else tx-decode-failed)"] },
    { m: ["gw", "db", "coins for inputs and own outputs, tip height"] },
    { m: ["gw", "dom", "Check(tx) with UTXO set + mempool view"] },
    { self: "dom", lines: ["CheckTransaction rules; no coinbase", "Not already in the mempool", "No input already spent by a mempool tx", "Inputs in UTXO set or mempool view", "Coinbase coins mature at tip + 1; fee ≥ 0", "P2PKH signatures verified; others left to Core"] },
    { alt: "the node rejects" },
    { m: ["api", "w", "400 { rejectedBy: node, code }"], reply: true, tone: "bad" },
    { m: ["api", "ui", "SignalR TxRejected + log with rule"], async: true },
    { alt: "else · the node accepts" },
    { m: ["gw", "core", "sendrawtransaction(hex)"] },
    { alt: "Core rejects" },
    { m: ["api", "w", "400 { rejectedBy: core, code: Core's reason }"], reply: true, tone: "bad" },
    { alt: "else · Core accepts" },
    { m: ["core", "gw", "txid"], reply: true },
    { self: "gw", lines: ["Mempool view: add the tx"] },
    { m: ["api", "ui", "SignalR TxAccepted (source: gateway)"], async: true },
    { m: ["api", "w", "200 { txid, fee, unverifiedInputs }"], reply: true },
  ],
  notes: ["A double spend is refused by the node with bip125-replacement-disallowed; Core never sees it.", "Codes are Core's reject reasons; refusals by Core carry rejectedBy: core."],
},
{
  id: "explorer", num: "05", group: "READING", title: "Explorer queries",
  lead: "The explorer reads only what the node itself validated. Each query reads PostgreSQL in one REPEATABLE READ snapshot, so a page never mixes two tips; pending data comes from the in-memory mempool view.",
  meta: [["Endpoints", "/chain/summary, /blocks, /tx, /mempool/entries, /addresses"], ["Code", "ExplorerQueries, ExplorerReadModel, AddressQueries"], ["Writes", "none"]],
  parts: [["ui", "Explorer", "browser, hash router", "js"], ["api", "Node API", "ExplorerQueries", "cs"], ["db", "PostgreSQL", "REPEATABLE READ", "infra"],
          ["view", "Mempool view", "MempoolState", "cs"], ["core", "Bitcoin Core", "regtest node", "cpp"]],
  steps: [
    { m: ["ui", "api", "GET /chain/summary"] },
    { m: ["api", "db", "tip, UTXO count and total"] },
    { m: ["api", "view", "mempool count"] },
    { m: ["api", "core", "getblockchaininfo: Core's height"] },
    { m: ["ui", "api", "GET /blocks · /blocks/{hash or height}"] },
    { m: ["api", "db", "blocks, txs, decoded inputs and outputs"] },
    { m: ["ui", "api", "GET /tx/{txid}"] },
    { m: ["api", "db", "confirmed: from the chain"] },
    { m: ["api", "view", "else: pending, from the view"] },
    { m: ["ui", "api", "GET /mempool/entries"] },
    { m: ["api", "view", "txs with fee, vsize, value"] },
    { m: ["ui", "api", "GET /addresses/{a}/utxos · /balance"] },
    { m: ["api", "db", "coins by scriptPubKey"] },
    { note: ["ui", "api"], text: "Views re-query on SignalR BlockConnected, TxAccepted, Reconnected" },
  ],
  notes: ["A coin is spendable when it is not coinbase, or its coinbase is mature at tip + 1.", "Only the summary asks Core anything, to show both tips side by side."],
},
{
  id: "parity", num: "06", group: "READING", title: "Parity check",
  lead: "Two independent implementations, one chain. The node compares what it computed with what Core computed: tip, the whole UTXO set, and the balance of any address you ask about.",
  meta: [["Endpoint", "GET /parity?address=…"], ["Code", "ParityCheck, UtxoStats"], ["Green when", "every check matches"]],
  parts: [["ui", "Explorer", "Parity page", "js"], ["api", "GET /parity", "ParityCheck", "cs"], ["db", "PostgreSQL", "REPEATABLE READ", "infra"], ["core", "Bitcoin Core", "regtest node", "cpp"]],
  steps: [
    { m: ["ui", "api", "GET /parity?address=…"] },
    { m: ["api", "db", "our tip, UTXO count, total value"] },
    { m: ["api", "core", "gettxoutsetinfo"] },
    { m: ["core", "api", "height, bestblock, txouts, total_amount"], reply: true },
    { alt: "for each address asked about" },
    { m: ["api", "db", "the address's coins"] },
    { m: ["api", "core", "scantxoutset addr(…)"] },
    { m: ["core", "api", "unspents, total_amount"], reply: true },
    { self: "api", lines: ["Compare ours with Core's, item by item", "Level only when the tips are equal"] },
    { m: ["api", "ui", "ParityReport { level, green, checks }"], reply: true },
  ],
  notes: ["The strongest single proof the node is right: Core's own count and total of the UTXO set agree with ours.", "scripts/utxo_diff.py goes one step further and diffs the two UTXO sets coin by coin."],
},
{
  id: "wallet", num: "07", group: "CLIENTS", title: "Browser wallet",
  lead: "A key generated in the visitor's browser and kept only there. The page builds and signs legacy P2PKH payments in JavaScript, byte for byte like the Python wallet, and sends them through the gateway.",
  meta: [["Page", "#/wallet, #/wallet/pay/{address}"], ["Code", "js/wallet/*, views/wallet.js"], ["Keys", "localStorage, never sent"]],
  parts: [["user", "Visitor", "phone or laptop", "js"], ["page", "Wallet page", "JavaScript", "js"], ["ls", "localStorage", "this browser", "infra"], ["api", "Node API", "MiniBtc.Api", "cs"]],
  steps: [
    { m: ["user", "page", "Create wallet (or import WIF)"] },
    { self: "page", lines: ["crypto.getRandomValues, key in [1, n − 1]", "compressed secp256k1 public key", "address = Base58Check(0x6f ‖ HASH160)"] },
    { m: ["page", "ls", "store the WIF"] },
    { m: ["page", "api", "GET /addresses/{a}/utxos"] },
    { m: ["api", "page", "coins, spendable flags, tip"], reply: true },
    { m: ["user", "page", "pay to, amount: Review"] },
    { self: "page", lines: ["Largest coins first, 1 sat/vB", "Change to self; under 546 sat goes to fee", "Legacy SIGHASH_ALL digest per input", "RFC 6979 nonce, low-S, DER + 01", "scriptSig = ‹sig› ‹pubkey›"] },
    { m: ["user", "page", "Broadcast"] },
    { m: ["page", "api", "POST /tx (flow 04)"] },
    { m: ["api", "page", "200 txid · or 400 rejectedBy"], reply: true },
    { m: ["page", "ls", "hide spent coins until the tip moves"] },
  ],
  notes: ["The Receive QR code is a link to #/wallet/pay/{address}: another phone's camera opens its own wallet with the recipient filled in.", "Signing needs Web Crypto, so the page needs HTTPS or localhost."],
},
{
  id: "faucet", num: "08", group: "CLIENTS", title: "Faucet",
  lead: "Gives a new browser wallet 5 regtest BTC. It runs in the Python demo service, which already holds keys, so the node still holds none. It pays through the gateway like any wallet, then mines a block so the coins are spendable at once.",
  meta: [["Endpoint", "POST /faucet, POST /faucet/mine (Caddy → demo:8090)"], ["Code", "py/demo/faucet.py, __main__.py"], ["Limits", "1 per address per 30 s, 6 per IP per 10 min"]],
  parts: [["page", "Wallet page", "browser", "js"], ["caddy", "Caddy", "/faucet* route", "infra"], ["demo", "Demo service", "Python, :8090", "py"], ["api", "Node API", "MiniBtc.Api", "cs"], ["core", "Bitcoin Core", "regtest node", "cpp"]],
  steps: [
    { m: ["page", "caddy", "POST /faucet { address }"] },
    { m: ["caddy", "demo", "reverse_proxy demo:8090"] },
    { self: "demo", lines: ["Rate limits by address, IP, everyone", "decode_address: regtest P2PKH or P2SH"] },
    { m: ["demo", "api", "GET /addresses/{miner}/utxos"] },
    { self: "demo", lines: ["5 BTC to the visitor, change to miner", "sign with the miner key"] },
    { m: ["demo", "api", "POST /tx (flow 04)"] },
    { m: ["api", "core", "sendrawtransaction"] },
    { m: ["demo", "core", "generatetoaddress(1, miner)"] },
    { m: ["core", "api", "ZMQ: block sync (flow 01)"], async: true },
    { m: ["demo", "api", "poll tipHeight until the node has the block"] },
    { m: ["demo", "page", "200 { txid, amount, height } via Caddy"], reply: true },
    { alt: "Mine a block button" },
    { m: ["page", "demo", "POST /faucet/mine → generatetoaddress(1) via Caddy"] },
  ],
  notes: ["Python serves /faucet; the browser calls it. Python's own calls go to the node's API and Core's RPC.", "Locally there is no faucet route; the page shows the miner command instead."],
},
{
  id: "demo", num: "09", group: "CLIENTS", title: "Demo traffic",
  lead: "Keeps the public explorer alive: Alice and Bob pay each other through the gateway, a block is mined every minute, and now and then a deliberate double spend shows the node refusing it before Core sees it.",
  meta: [["Runs", "docker compose service demo"], ["Code", "py/demo/__main__.py"], ["Keys", "Alice, Bob, miner (fixed regtest keys)"]],
  parts: [["demo", "Demo loop", "Python", "py"], ["api", "Node API", "MiniBtc.Api", "cs"], ["core", "Bitcoin Core", "regtest node", "cpp"]],
  steps: [
    { alt: "every ~20 s" },
    { m: ["demo", "api", "GET /addresses/{payer}/utxos"] },
    { self: "demo", lines: ["0.10–2.00 BTC, Alice ↔ Bob", "sign with the payer's key"] },
    { m: ["demo", "api", "POST /tx (flow 04)"] },
    { m: ["api", "core", "sendrawtransaction"] },
    { alt: "15% of rounds · deliberate double spend" },
    { m: ["demo", "api", "POST /tx spending a coin already spent"] },
    { m: ["api", "demo", "400 rejectedBy node: bip125-replacement-disallowed"], reply: true, tone: "bad" },
    { alt: "every ~60 s" },
    { m: ["demo", "core", "generatetoaddress(1, miner)"] },
    { note: ["demo", "core"], text: "If Alice + Bob hold under 1 BTC, or the miner under 10 BTC, mine 101 blocks so a coinbase matures" },
  ],
  notes: ["One lock serializes the loop and faucet requests, so they never pick the same coin.", "Spent outpoints stay hidden until the tip moves, so the loop does not double spend by accident."],
},
{
  id: "cli", num: "10", group: "CLIENTS", title: "CLI wallet and miner",
  lead: "The first clients of the node, used in development and tests: a Python wallet that keeps keys in a local file and signs with coincurve, and a miner that asks Core to mine.",
  meta: [["Commands", "python -m wallet …, python -m miner generate"], ["Code", "py/wallet, py/miner, py/btc_rpc"], ["Keys", "local file, never sent"]],
  parts: [["dev", "Developer", "terminal", "infra"], ["cli", "Python CLI", "wallet / miner", "py"], ["api", "Node API", "MiniBtc.Api", "cs"], ["core", "Bitcoin Core", "regtest node", "cpp"]],
  steps: [
    { alt: "mining" },
    { m: ["dev", "cli", "miner generate 101 --to ‹address›"] },
    { m: ["cli", "core", "generatetoaddress(101, address)"] },
    { m: ["core", "api", "ZMQ: block sync (flow 01)"], async: true },
    { alt: "paying" },
    { m: ["dev", "cli", "wallet send ‹address› ‹amount›"] },
    { m: ["cli", "api", "GET /addresses/{a}/utxos"] },
    { self: "cli", lines: ["build_payment: largest first, 1 sat/vB", "sign_p2pkh: SIGHASH_ALL, low-S DER"] },
    { m: ["cli", "api", "POST /tx (flow 04)"] },
    { m: ["api", "cli", "txid · or Rejected(rejectedBy, code)"], reply: true },
  ],
  notes: ["Signing is verified against bitcoinjs-lib's vectors; the browser wallet produces the same bytes.", "--dry-run prints the raw transaction for the Broadcast page instead of sending it."],
},
];

const SVGNS = "http://www.w3.org/2000/svg";
const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const ctx = document.createElement("canvas").getContext("2d");
const cssVar = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
let SANS = "sans-serif", MONO = "monospace";
const measure = (text, font) => { ctx.font = font; return ctx.measureText(text).width; };
const F_MSG = () => `500 12px ${SANS}`;
const F_NOTE = () => `400 12px ${SANS}`;
const F_SELF = () => `400 11.5px ${MONO}`;

function wrap(text, max, font) {
  const words = text.split(" "); const lines = []; let line = "";
  for (const w of words) { const next = line ? line + " " + w : w; if (measure(next, font) > max && line) { lines.push(line); line = w; } else line = next; }
  if (line) lines.push(line); return lines;
}

function sequence(flow) {
  const GAP = 150, BOX = 136, M = 16, HEAD = 50;
  const ids = flow.parts.map((p) => p[0]);
  const x = (id) => M + BOX / 2 + ids.indexOf(id) * GAP;
  const W = M * 2 + BOX + (ids.length - 1) * GAP;
  let y = HEAD + 24, n = 0;
  const out = [];
  for (const s of flow.steps) {
    if (s.m) {
      const [f, t, label] = s.m; n++;
      y += 36;
      const x1 = x(f), x2 = x(t), dir = x2 > x1 ? 1 : -1;
      const cls = s.reply ? "reply" : s.async ? "async" : "call";
      const color = s.tone === "bad" ? "var(--bad)" : "var(--text-2)";
      const dash = s.reply ? ' stroke-dasharray="5 4"' : s.async ? ' stroke-dasharray="1.5 3.5"' : "";
      out.push(`<line x1="${x1 + dir * 2}" y1="${y}" x2="${x2 - dir * 6}" y2="${y}" stroke="${color}" stroke-width="${s.async ? 1.6 : 1.2}"${dash} marker-end="url(#${s.tone === "bad" ? "head-bad" : cls === "reply" ? "head-open" : "head"})"/>`);
      const tw = measure(label, F_MSG()), pill = 20, total = pill + 6 + tw;
      const cx = (x1 + x2) / 2, left = Math.max(4, Math.min(W - total - 4, cx - total / 2));
      out.push(`<rect x="${left - 4}" y="${y - 22}" width="${total + 8}" height="17" rx="3" fill="var(--surface)"/>`);
      out.push(`<rect x="${left}" y="${y - 21}" width="${pill}" height="15" rx="7.5" fill="${s.tone === "bad" ? "var(--bad)" : "var(--text)"}"/>`);
      out.push(`<text x="${left + pill / 2}" y="${y - 10}" text-anchor="middle" font-size="10" font-weight="600" fill="var(--surface)" style="font-family:var(--font-mono)">${n}</text>`);
      out.push(`<text x="${left + pill + 6}" y="${y - 9.5}" font-size="12" font-weight="500" fill="${s.tone === "bad" ? "var(--bad)" : "var(--text)"}">${esc(label)}</text>`);
    } else if (s.self) {
      const width = Math.max(150, ...s.lines.map((l) => measure(l, F_SELF()))) + 24;
      const h = s.lines.length * 17 + 12; y += 14;
      const k = flow.parts.find((p) => p[0] === s.self)[3];
      const left = Math.max(2, Math.min(W - width - 2, x(s.self) - width / 2));
      out.push(`<rect x="${left}" y="${y}" width="${width}" height="${h}" rx="4" fill="var(--${k}-fill)" stroke="var(--${k}-edge)"/>`);
      s.lines.forEach((l, i) => out.push(`<text x="${left + 12}" y="${y + 19 + i * 17}" font-size="11.5" fill="var(--text-2)" style="font-family:var(--font-mono)">${esc(l)}</text>`));
      y += h;
    } else if (s.note) {
      const a = Math.min(x(s.note[0]), x(s.note[1])), b = Math.max(x(s.note[0]), x(s.note[1]));
      const pad = a === b ? 150 : BOX / 2; const left = Math.max(2, a - pad), right = Math.min(W - 2, b + pad), width = right - left;
      const lines = wrap(s.text, width - 24, F_NOTE()), h = lines.length * 16 + 12; y += 14;
      const bad = s.tone === "bad";
      out.push(`<rect x="${left}" y="${y}" width="${width}" height="${h}" rx="4" fill="${bad ? "var(--bad-fill)" : "var(--note-fill)"}" stroke="${bad ? "var(--bad)" : "var(--note-edge)"}" stroke-dasharray="${bad ? "0" : "3 3"}"/>`);
      lines.forEach((l, i) => out.push(`<text x="${left + 12}" y="${y + 18 + i * 16}" font-size="12" fill="${bad ? "var(--bad)" : "var(--text-2)"}">${esc(l)}</text>`));
      y += h;
    } else if (s.alt) {
      y += 30;
      const tag = s.alt.toUpperCase(), tw = measure(tag, `500 10.5px ${MONO}`) + 16;
      out.push(`<line x1="0" y1="${y}" x2="${W}" y2="${y}" stroke="var(--border-strong)" stroke-dasharray="6 4"/>`);
      out.push(`<rect x="0" y="${y - 10}" width="${tw}" height="20" rx="3" fill="var(--surface-2)" stroke="var(--border-strong)"/>`);
      out.push(`<text x="8" y="${y + 4}" font-size="10.5" font-weight="500" letter-spacing=".04em" fill="var(--text-2)" style="font-family:var(--font-mono)">${esc(tag)}</text>`);
      y += 4;
    }
  }
  const H = y + 26;
  const heads = flow.parts.map(([id, name, sub, k]) => {
    const cx = x(id);
    return `<line x1="${cx}" y1="${HEAD}" x2="${cx}" y2="${H}" stroke="var(--${k}-edge)" stroke-dasharray="2 4"/>
      <rect x="${cx - BOX / 2}" y="0" width="${BOX}" height="${HEAD}" rx="5" fill="var(--${k}-fill)" stroke="var(--${k}-edge)"/>
      <rect x="${cx - BOX / 2}" y="0" width="${BOX}" height="3" fill="var(--${k})" opacity=".85"/>
      <text x="${cx}" y="23" text-anchor="middle" font-size="13" font-weight="600" fill="var(--text)">${esc(name)}</text>
      <text x="${cx}" y="39" text-anchor="middle" font-size="9.5" fill="var(--text-2)" style="font-family:var(--font-mono)">${esc(sub)}</text>`;
  }).join("");
  return `<svg xmlns="${SVGNS}" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" role="img" aria-label="${esc(flow.title)} sequence diagram">${defs()}${heads}${out.join("")}</svg>`;
}

function defs() {
  return `<defs>
    <marker id="head" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse"><path d="M1 1 L9 5 L1 9 z" fill="var(--text-2)"/></marker>
    <marker id="head-open" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse"><path d="M1 1 L9 5 L1 9" fill="none" stroke="var(--text-2)" stroke-width="1.4"/></marker>
    <marker id="head-bad" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse"><path d="M1 1 L9 5 L1 9" fill="none" stroke="var(--bad)" stroke-width="1.4"/></marker>
  </defs>`;
}

// ---- System map: hand-placed containers, edges routed around boxes.
function systemMap() {
  const box = (x, y, w, h, k, title, sub) => `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="5" fill="var(--${k}-fill)" stroke="var(--${k}-edge)"/>
    <rect x="${x}" y="${y}" width="4" height="${h}" fill="var(--${k})" opacity=".85"/>
    <text x="${x + 16}" y="${y + h / 2 - 3}" font-size="13" font-weight="600" fill="var(--text)">${esc(title)}</text>
    <text x="${x + 16}" y="${y + h / 2 + 14}" font-size="10.5" fill="var(--text-2)" style="font-family:var(--font-mono)">${esc(sub)}</text>`;
  const label = (x, y, t, anchor = "start") => `<text x="${x}" y="${y}" text-anchor="${anchor}" font-size="11.5" font-weight="500" fill="var(--text-2)">${esc(t)}</text>`;
  const edge = (d, opts = "") => `<path d="${d}" fill="none" stroke="var(--text-3)" stroke-width="1.2" marker-end="url(#head)" ${opts}/>`;
  const both = (d) => edge(d, 'marker-start="url(#head)"');
  return `<svg xmlns="${SVGNS}" width="1060" height="600" viewBox="0 0 1060 600" role="img" aria-label="System map of the deployment">${defs()}
    ${box(60, 20, 230, 60, "js", "Visitor's browser", "explorer + wallet (JS)")}
    ${box(60, 150, 230, 56, "infra", "Caddy", "HTTPS :443, routes paths")}
    <rect x="340" y="116" width="390" height="330" rx="8" fill="none" stroke="var(--cs-edge)" stroke-dasharray="6 4"/>
    <text x="356" y="138" font-size="11" font-weight="500" letter-spacing=".06em" fill="var(--cs)" style="font-family:var(--font-mono)">MINIBTC NODE · .NET 10 · ONE PROCESS</text>
    ${box(360, 152, 350, 52, "cs", "API + SignalR hub", "REST endpoints, /hubs/chain, static UI")}
    ${box(360, 218, 350, 52, "cs", "Sync worker + ZMQ listeners", "ChainSynchronizer, MempoolSynchronizer")}
    ${box(360, 284, 350, 52, "cs", "Domain", "consensus rules, UtxoSet, mempool view")}
    ${box(360, 350, 350, 52, "cs", "Messaging", "MassTransit EF outbox, consumers")}
    ${box(780, 140, 240, 60, "py", "Python services and tools", "demo + faucet :8090, CLI")}
    ${box(780, 226, 240, 60, "cpp", "Bitcoin Core", "regtest · RPC + ZMQ")}
    ${box(360, 490, 160, 56, "infra", "PostgreSQL", "chain, UTXOs, outbox")}
    ${box(550, 490, 160, 56, "infra", "RabbitMQ", "integration events")}
    ${both("M 175 82 L 175 148")}${label(184, 120, "HTTPS, WebSocket")}
    ${both("M 292 178 L 358 178")}
    ${edge("M 778 172 L 712 172")}${label(745, 164, "POST /tx", "middle")}
    ${both("M 778 252 L 712 252")}${label(745, 244, "RPC, ZMQ", "middle")}
    ${edge("M 900 202 L 900 224")}${label(908, 217, "RPC generatetoaddress")}
    ${both("M 440 404 L 440 488")}${label(448, 470, "EF Core")}
    ${both("M 630 404 L 630 488")}${label(638, 470, "AMQP")}
    ${edge("M 175 208 L 175 572 L 1040 572 L 1040 170 L 1022 170")}${label(186, 564, "/faucet* only → demo:8090")}
  </svg>`;
}


const CATALOG = `<div class="arch-tables">
        <div><h3>HTTP endpoints</h3><table class="arch-catalog"><thead><tr><th>Endpoint</th><th>Served by · used in</th></tr></thead><tbody>
          <tr><td class="mono">POST /tx</td><td>Broadcast gateway · 04, 07, 08, 09, 10</td></tr>
          <tr><td class="mono">GET /addresses/{a}/utxos</td><td>Coins with spendable flags · 05, 07–10</td></tr>
          <tr><td class="mono">GET /addresses/{a}/balance</td><td>Spendable, immature, total · 05, 08</td></tr>
          <tr><td class="mono">GET /chain/summary</td><td>Tip, UTXO stats, Core height · 05</td></tr>
          <tr><td class="mono">GET /blocks, /blocks/{id}</td><td>Block list and detail · 05</td></tr>
          <tr><td class="mono">GET /tx/{txid}</td><td>Confirmed or pending tx · 05</td></tr>
          <tr><td class="mono">GET /mempool, /mempool/entries</td><td>Mempool view · 05</td></tr>
          <tr><td class="mono">GET /parity</td><td>Node vs Core comparison · 06</td></tr>
          <tr><td class="mono">GET /health</td><td>Liveness + Core reachability</td></tr>
          <tr><td class="mono">/hubs/chain</td><td>SignalR hub, live events</td></tr>
          <tr><td class="mono">POST /faucet, /faucet/mine</td><td>Demo service (Python), via Caddy · 08</td></tr>
        </tbody></table></div>
        <div><h3>Events</h3><table class="arch-catalog"><thead><tr><th>Event</th><th>Producer → consumer</th></tr></thead><tbody>
          <tr><td class="mono">BlockAnnounced</td><td>ZMQ listeners → sync worker (RabbitMQ) · 01, 02</td></tr>
          <tr><td class="mono">BlockConnectedV1</td><td>UtxoSet via EF outbox → hub relay, log (RabbitMQ) · 01</td></tr>
          <tr><td class="mono">BlockDisconnectedV1</td><td>UtxoSet via EF outbox → hub relay (RabbitMQ) · 02</td></tr>
          <tr><td class="mono">MempoolTxAdded / Removed</td><td>ZMQ sequence → mempool consumer (RabbitMQ) · 03</td></tr>
          <tr><td class="mono">BlockConnected, BlockDisconnected</td><td>Hub → browsers (SignalR) · 01, 02</td></tr>
          <tr><td class="mono">TxAccepted</td><td>Gateway or mempool consumer → browsers (SignalR) · 03, 04</td></tr>
          <tr><td class="mono">TxRejected</td><td>Gateway → browsers (SignalR) · 04</td></tr>
          <tr><td class="mono">TxRemoved</td><td>Mempool consumer → browsers (SignalR) · 03</td></tr>
        </tbody></table></div>
      </div>`;
const LEGEND = `<div><span class="sw" style="background:var(--cs-fill);border-color:var(--cs-edge)"></span>Node, C# / .NET 10</div>
      <div><span class="sw" style="background:var(--py-fill);border-color:var(--py-edge)"></span>Python services and tools</div>
      <div><span class="sw" style="background:var(--js-fill);border-color:var(--js-edge)"></span>Browser, JavaScript</div>
      <div><span class="sw" style="background:var(--cpp-fill);border-color:var(--cpp-edge)"></span>Bitcoin Core, C++</div>
      <div><span class="sw" style="background:var(--infra-fill);border-color:var(--infra-edge)"></span>Infrastructure</div>`;

export async function mount(root, [section] = []) {
  SANS = cssVar("--font-sans") || SANS;
  MONO = cssVar("--font-mono") || MONO;
  root.innerHTML = html`${pageHead({
    title: "Architecture",
    sub: "Every flow the application supports, drawn from the code. The node follows Bitcoin Core and re-validates everything with its own consensus code; it holds no keys. Solid arrows are calls, dashed arrows replies, dotted arrows asynchronous events.",
  })}
  <div class="arch">
    <div class="arch-index">
      <a class="chip-link" href="#/architecture/map">00 System map</a>
      <a class="chip-link" href="#/architecture/catalog">Interfaces and events</a>
      ${raw(FLOWS.map((f) => `<a class="chip-link" href="#/architecture/${f.id}">${f.num} ${esc(f.title)}</a>`).join(""))}
    </div>
    <div class="arch-legend">${raw(LEGEND)}</div>
    <section class="arch-card" id="arch-map">
      <div class="arch-head"><div><div class="id">00 · CONTAINERS</div><h2>System map</h2>
        <p>What runs where on the one-server deployment. Only Caddy is reachable from the internet; Core, PostgreSQL and RabbitMQ stay on the Docker network.</p></div>
        <dl class="arch-meta"><dt>Host</dt><dd>one VM, Docker Compose</dd><dt>Edge</dt><dd>Caddy, automatic HTTPS</dd><dt>Chain</dt><dd>regtest</dd></dl></div>
      <div class="arch-diagram">${raw(systemMap())}</div>
    </section>
    <section class="arch-card" id="arch-catalog">
      <div class="arch-head"><div><div class="id">INTERFACES</div><h2>Interfaces and events</h2>
        <p>The contracts the flows are made of: the node's HTTP endpoints, the integration events on RabbitMQ, and the live events pushed to browsers.</p></div></div>
      ${raw(CATALOG)}
    </section>
    ${raw(FLOWS.map((f) => `
    <section class="arch-card" id="arch-${f.id}">
      <div class="arch-head"><div><div class="id">${f.num} · ${f.group}</div><h2>${esc(f.title)}</h2><p>${esc(f.lead)}</p></div>
        <dl class="arch-meta">${f.meta.map(([k, v]) => `<dt>${esc(k)}</dt><dd>${esc(v)}</dd>`).join("")}</dl></div>
      <div class="arch-diagram">${sequence(f)}</div>
      <div class="arch-notes">${f.notes.map((n) => `<div>${esc(n)}</div>`).join("")}</div>
    </section>`).join(""))}
  </div>`.html;
  if (section) root.querySelector(`#arch-${CSS.escape(section)}`)?.scrollIntoView({ block: "start" });
}
