// Overview: the chain at a glance, kept current by live events.
import { api } from "../api.js";
import { btc, bytes, clock, feeRate, html, number, raw, shortHash, timeAgo } from "../format.js";
import * as live from "../live.js";
import { amount, badge, card, errorState, hashLink, icons, loading, pageHead, table } from "../ui.js";

export async function mount(root) {
  root.innerHTML = html`${pageHead({
    title: "Overview",
    sub: "The regtest chain as our node validated it, with its own consensus code, next to Bitcoin Core's view.",
  })}
  <div id="kpis" class="grid kpis">${loading(2)}${loading(2)}${loading(2)}${loading(2)}</div>
  <div class="grid main-side" style="margin-top:16px">
    <div class="stack"><div id="latest">${loading(6)}</div><div id="pending">${loading(4)}</div></div>
    <div class="stack"><div id="parity">${loading(3)}</div><div id="feed"></div></div>
  </div>`.html;

  const refreshAll = () => Promise.all([renderKpis(root), renderBlocks(root), renderMempool(root), renderParity(root)]);
  let pending = null;
  const refreshSoon = (fn) => { clearTimeout(pending); pending = setTimeout(fn, 250); };

  renderFeed(root);
  await refreshAll();

  const subscriptions = [
    live.on("BlockConnected", () => refreshAll()),
    live.on("BlockDisconnected", () => refreshAll()),
    live.on("TxAccepted", () => refreshSoon(() => Promise.all([renderKpis(root), renderMempool(root)]))),
    live.on("TxRemoved", () => refreshSoon(() => Promise.all([renderKpis(root), renderMempool(root)]))),
    live.on("Reconnected", () => refreshAll()),
    live.on("*", () => renderFeed(root)),
  ];
  const ticker = setInterval(() => renderBlocks(root, { quiet: true }), 30000); // keep "x ago" honest
  return () => { subscriptions.forEach((off) => off()); clearInterval(ticker); clearTimeout(pending); };
}

async function renderKpis(root) {
  const host = root.querySelector("#kpis");
  try {
    const s = await api.summary();
    const synced = s.coreHeight != null && s.coreHeight === s.tipHeight;
    host.innerHTML = html`
      ${kpi("Node tip", number(s.tipHeight), html`<a class="hash" href="#/block/${s.tipHash}">${shortHash(s.tipHash, 8, 6)}</a> · ${timeAgo(s.tipTime)}`)}
      ${kpi("Bitcoin Core tip", s.coreHeight == null ? "—" : number(s.coreHeight),
        s.coreHeight == null ? badge("Unreachable", "bad") : synced ? badge("In sync", "ok") : badge(`${s.coreHeight - s.tipHeight} behind`, "warn"))}
      ${kpi("UTXO set", raw(`${btc(s.utxoTotal, { trim: true })}<small>BTC</small>`), `${number(s.utxoCount)} unspent outputs`)}
      ${kpi("Mempool", number(s.mempoolCount), html`<a href="#/mempool">${s.mempoolCount === 1 ? "transaction" : "transactions"} waiting →</a>`)}`.html;
  } catch (error) {
    host.innerHTML = errorState("Node unreachable", error.message).html;
  }
}

const kpi = (label, value, meta) =>
  html`<div class="card kpi"><div class="kpi-label">${label}</div><div class="kpi-value">${value}</div><div class="kpi-meta">${meta}</div></div>`;

let lastTop = null;
async function renderBlocks(root, { quiet = false } = {}) {
  const host = root.querySelector("#latest");
  try {
    const blocks = await api.blocks(10);
    const newTop = blocks[0]?.hash;
    host.innerHTML = card({
      title: "Latest blocks",
      hint: "Validated and committed by the node, one database transaction per block",
      actions: html`<a class="btn" href="#/blocks">View all</a>`,
      body: table([
        { label: "Height", render: (b) => html`<a href="#/block/${b.hash}"><strong>${number(b.height)}</strong></a>` },
        { label: "Hash", render: (b) => hashLink("block", b.hash) },
        { label: "Mined", render: (b) => html`<span title="${b.time}">${timeAgo(b.time)}</span>` },
        { label: "Txs", num: true, render: (b) => number(b.transactionCount) },
        { label: "Size", num: true, render: (b) => bytes(b.size) },
        { label: "Fees", num: true, render: (b) => amount(b.fees, { trim: true }) },
      ], blocks, {
        empty: "No blocks yet. Mine some with `python -m miner generate 101 --to <address>`.",
        rowAttrs: (b) => (!quiet && lastTop && b.hash === newTop && newTop !== lastTop ? 'class="flash"' : ""),
      }),
    }).html;
    lastTop = newTop;
  } catch (error) {
    host.innerHTML = errorState("Blocks unavailable", error.message).html;
  }
}

async function renderMempool(root) {
  const host = root.querySelector("#pending");
  try {
    const entries = await api.mempool();
    host.innerHTML = card({
      title: "Mempool",
      hint: "Waiting for a block, as the node sees Core's mempool",
      actions: html`<a class="btn" href="#/mempool">View all</a>`,
      body: table([
        { label: "Txid", render: (t) => hashLink("tx", t.txid) },
        { label: "Fee rate", num: true, render: (t) => feeRate(t.fee, t.vsize) },
        { label: "Vsize", num: true, render: (t) => `${number(t.vsize)} vB` },
        { label: "Value", num: true, render: (t) => amount(t.totalOut, { trim: true }) },
      ], entries.slice(0, 8), { empty: "The mempool is empty. Send a payment with the wallet or the Broadcast page." }),
      foot: entries.length > 8 ? html`<span>${entries.length - 8} more</span><a href="#/mempool">Open mempool →</a>` : "",
    }).html;
  } catch (error) {
    host.innerHTML = errorState("Mempool unavailable", error.message).html;
  }
}

async function renderParity(root) {
  const host = root.querySelector("#parity");
  try {
    const report = await api.parity();
    const tone = report.green ? "ok" : report.level ? "bad" : "warn";
    const title = report.green ? "Node and Core agree" : report.level ? "Node and Core disagree" : "Node is catching up";
    host.innerHTML = card({
      title: "Parity with Bitcoin Core",
      actions: html`<a class="btn" href="#/parity">Details</a>`,
      body: html`<div class="card-body stack">
        <div class="banner ${tone}"><div><strong>${title}</strong><span class="text">Checked ${clock(new Date(report.checkedAt))}: tip, UTXO count and total value.</span></div></div>
        ${table([
          { label: "Check", render: (c) => c.name },
          { label: "", num: true, render: (c) => (c.match ? badge("Match", "ok") : badge("Differs", "bad")) },
        ], report.checks)}
      </div>`,
    }).html;
  } catch (error) {
    host.innerHTML = card({ title: "Parity with Bitcoin Core", body: errorState("Parity check failed", error.message) }).html;
  }
}

function renderFeed(root) {
  const host = root.querySelector("#feed");
  const items = live.history.slice(0, 25);
  host.innerHTML = card({
    title: "Live activity",
    hint: "Pushed over SignalR from the outbox and Core's ZMQ events",
    body: items.length
      ? html`<ul class="feed">${items.map(feedItem)}</ul>`
      : html`<div class="empty">Waiting for events. Mine a block or send a transaction.</div>`,
  }).html;
}

function feedItem({ event, payload, at }) {
  const entry = {
    BlockConnected: ["block", icons.block, `Block ${number(payload.height)} connected`, html`${payload.transactionCount} tx · <a class="hash" href="#/block/${payload.hash}">${shortHash(payload.hash)}</a>`],
    BlockDisconnected: ["warn", icons.undo, `Block ${number(payload.height)} disconnected`, html`Reorg · ${shortHash(payload.hash)}`],
    TxAccepted: ["tx", icons.tx, payload.source === "gateway" ? "Broadcast accepted" : "Entered the mempool", html`<a class="hash" href="#/tx/${payload.txid}">${shortHash(payload.txid)}</a>`],
    TxRemoved: ["tx", icons.x, "Left the mempool", html`${shortHash(payload.txid)}`],
    TxRejected: ["bad", icons.x, `Rejected by ${payload.rejectedBy}`, html`<code>${payload.code}</code>`],
  }[event];
  if (!entry) return "";
  const [tone, icon, title, detail] = entry;
  return html`<li><span class="icon ${tone}">${icon}</span><div><div class="title">${title}</div><div class="detail">${detail}</div></div><time>${clock(at)}</time></li>`;
}
