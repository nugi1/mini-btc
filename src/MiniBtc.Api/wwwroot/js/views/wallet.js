// Wallet: a regtest key that lives only in this browser. It signs legacy P2PKH payments in
// JavaScript (js/wallet/, byte-identical to the Python wallet) and sends them through POST /tx.
import { api, faucet } from "../api.js";
import { btc, html, number, raw, shortHash, timeAgo } from "../format.js";
import * as live from "../live.js";
import { amount, badge, card, copyButton, errorState, hashLink, icons, kv, loading, pageHead, table, toast } from "../ui.js";
import * as wallet from "../wallet/wallet.js";
import { InsufficientFunds } from "../wallet/builder.js";

const FAUCET_HINT = "python -m miner generate 101 --to";

export async function mount(root, [payTo] = []) {
  const state = { key: null, coins: null, prepared: null, faucet: null, revealed: false, confirmForget: false };
  const prefill = { to: payTo ?? "", amount: "" };

  root.innerHTML = html`${pageHead({
    title: "Wallet",
    sub: "A key generated in this browser and kept only here. Payments are signed in JavaScript and sent through the node's gateway, which checks them before Bitcoin Core does.",
  })}<div id="wallet">${loading(4)}</div>`.html;
  const host = root.querySelector("#wallet");

  if (!wallet.supported()) {
    host.innerHTML = errorState("This page needs HTTPS", "Signing uses the browser's Web Crypto, which works only over HTTPS or on localhost.").html;
    return;
  }

  faucet.health().then(() => { state.faucet = true; }, () => { state.faucet = false; }).finally(() => state.key && render());

  try { state.key = await wallet.load(); }
  catch { wallet.forget(); }

  async function refresh() {
    if (!state.key) return render();
    try { state.coins = await wallet.coins(state.key); state.error = null; }
    catch (error) { state.error = error.message; }
    render();
  }

  function render() {
    host.innerHTML = (state.key ? walletView(state, prefill) : welcomeView()).html;
    if (state.key) renderQr(host.querySelector("#qr"), state.key.address);
  }

  // ---- Every button on the page, by data-action.
  host.addEventListener("click", async (event) => {
    const button = event.target.closest("[data-action]");
    if (!button) return;
    const action = button.dataset.action;
    const busy = (on) => { button.disabled = on; button.classList.toggle("busy", on); };

    if (action === "create") {
      state.key = await wallet.Key.generate();
      if (!wallet.save(state.key)) toast("Storage is blocked", "The key will be lost when this tab closes. Back it up below.", "bad");
      toast("Wallet created", state.key.address, "ok");
      return refresh();
    }
    if (action === "faucet") {
      busy(true);
      try {
        const reply = await faucet.fund(state.key.address);
        toast("Faucet paid you", `${btc(reply.amount, { trim: true })} BTC · confirmed in block ${number(reply.height)}`, "ok");
      } catch (error) {
        toast("Faucet refused", error.message, "bad");
      } finally { busy(false); }
      return refresh();
    }
    if (action === "mine") {
      busy(true);
      try { const reply = await faucet.mine(); toast("Block mined", `Height ${number(reply.height)}`, "accent"); }
      catch (error) { toast("Mining refused", error.message, "bad"); }
      finally { busy(false); }
      return;
    }
    if (action === "max") {
      const input = host.querySelector("#amount");
      const usable = state.coins.coins.filter((c) => c.spendable && !c.pending);
      const fee = BigInt(10 + 148 * usable.length + 34);
      const max = state.coins.spendable - fee;
      input.value = max > 0n ? btc(max) : "";
      prefill.amount = input.value;
      return;
    }
    if (action === "cancel") { state.prepared = null; return render(); }
    if (action === "broadcast") {
      busy(true);
      try {
        const receipt = await wallet.broadcast(state.prepared, state.coins.tipHeight);
        toast("Payment sent", `${btc(state.prepared.amount, { trim: true })} BTC · ${receipt.txid}`, "ok");
        state.prepared = null;
        prefill.to = ""; prefill.amount = "";
        if (location.hash.startsWith("#/wallet/pay/")) history.replaceState(null, "", "#/wallet");
      } catch (error) {
        const body = error.body ?? {};
        state.prepared.error = body.rejectedBy
          ? { title: `Rejected by ${body.rejectedBy === "node" ? "the node" : "Bitcoin Core"}`, code: body.code, message: body.error }
          : { title: "Request failed", message: error.message };
      }
      return refresh();
    }
    if (action === "reveal") { state.revealed = !state.revealed; return render(); }
    if (action === "forget") {
      if (!state.confirmForget) { state.confirmForget = true; render(); setTimeout(() => { state.confirmForget = false; state.key && render(); }, 5000); return; }
      wallet.forget();
      Object.assign(state, { key: null, coins: null, prepared: null, revealed: false, confirmForget: false });
      toast("Wallet removed from this browser", "", "");
      return render();
    }
  });

  // ---- Forms: import a key, prepare a payment.
  host.addEventListener("submit", async (event) => {
    event.preventDefault();
    const form = event.target;
    if (form.id === "import") {
      try {
        state.key = await wallet.Key.fromWif(form.querySelector("#wif").value);
        wallet.save(state.key);
        toast("Wallet imported", state.key.address, "ok");
        return refresh();
      } catch (error) { return showFieldError(form, error.message); }
    }
    if (form.id === "send") {
      prefill.to = form.querySelector("#to").value.trim();
      prefill.amount = form.querySelector("#amount").value.trim();
      const sats = wallet.parseBtc(prefill.amount);
      if (!prefill.to) return showFieldError(form, "Enter the address to pay.");
      if (sats == null || sats <= 0n) return showFieldError(form, "Enter an amount in BTC, at most 8 decimals.");
      if (prefill.to === state.key.address) return showFieldError(form, "That is this wallet's own address.");
      try {
        state.prepared = await wallet.prepare(state.key, state.coins, prefill.to, sats);
        render();
      } catch (error) {
        showFieldError(form, error instanceof InsufficientFunds ? `Not enough confirmed coins. ${error.message}` : error.message);
      }
    }
  });

  host.addEventListener("input", (event) => {
    if (event.target.id === "to") prefill.to = event.target.value;
    if (event.target.id === "amount") prefill.amount = event.target.value;
  });

  await refresh();

  let pending = null;
  const soon = () => { clearTimeout(pending); pending = setTimeout(refresh, 300); };
  const subscriptions = [
    live.on("BlockConnected", refresh),
    live.on("BlockDisconnected", refresh),
    live.on("Reconnected", refresh),
    live.on("TxAccepted", soon),
  ];
  return () => { subscriptions.forEach((off) => off()); clearTimeout(pending); };
}

function showFieldError(form, message) {
  const slot = form.querySelector(".form-error");
  slot.textContent = message;
  slot.hidden = false;
}

// ---------------------------------------------------------------------------------------------

function welcomeView() {
  return html`<div class="grid two">
    ${card({
      title: "Create a wallet",
      hint: "A fresh secp256k1 key from the browser's CSPRNG",
      body: html`<div class="card-body stack">
        <p class="muted">The private key never leaves this browser: the node and Bitcoin Core only ever see signed transactions. Open this page on another phone to get a second wallet and pay between them.</p>
        <div class="toolbar"><button class="btn primary" type="button" data-action="create">Create wallet</button></div>
      </div>`,
    })}
    ${card({
      title: "Import a key",
      hint: "A regtest WIF, e.g. from python -m wallet",
      body: html`<form id="import" class="card-body stack" autocomplete="off">
        <div class="field"><label for="wif">Private key (WIF)</label>
          <input class="input mono" id="wif" spellcheck="false" placeholder="cMahea7zqjxrtgAbB7LSGbcQUr1uX1ojuat9jZodMN87JcbXMTcA">
          <div class="form-error" role="alert" hidden></div></div>
        <div class="toolbar"><button class="btn" type="submit">Import</button></div>
      </form>`,
    })}
  </div>
  <div class="banner warn" style="margin-top:16px"><div><strong>Regtest coins only</strong><span class="text">This chain is private to the demo and its coins have no value. Never import a key that holds real bitcoin.</span></div></div>`;
}

function walletView(state, prefill) {
  const { key, coins } = state;
  const kpi = (label, value, meta) =>
    html`<div class="card kpi"><div class="kpi-label">${label}</div><div class="kpi-value">${value}</div><div class="kpi-meta">${meta}</div></div>`;
  const big = (sats) => raw(`${btc(sats ?? 0n, { trim: true })}<small>BTC</small>`);

  return html`
  ${state.error ? html`<div class="banner bad" style="margin-bottom:16px"><div><strong>Node unreachable</strong><span class="text">${state.error}</span></div></div>` : ""}
  <div class="grid kpis">
    ${kpi("Spendable", big(coins?.spendable), "Confirmed and mature")}
    ${kpi("Immature", big(coins?.immature), "Coinbase rewards wait 100 blocks")}
    ${kpi("Pending", big(coins?.pending), coins?.pending ? "Spent by you, confirms with the next block" : "Nothing unconfirmed")}
    ${kpi("Coins", number(coins?.coins.length ?? 0), coins ? `at node tip ${number(coins.tipHeight)}` : "—")}
  </div>

  <div class="grid main-side" style="margin-top:16px">
    <div class="stack">
      ${state.prepared ? reviewCard(state.prepared) : sendCard(state, prefill)}
      ${coinsCard(coins)}
      ${sentCard()}
    </div>
    <div class="stack">
      ${card({
        title: "Receive",
        hint: "Scan with another phone's camera to pay this wallet",
        body: html`<div class="card-body stack">
          <div class="qr" id="qr" aria-label="QR code of the payment link"></div>
          <div class="address-box"><span class="hash">${key.address}</span>${copyButton(key.address)}</div>
          <div class="toolbar">${faucetButtons(state)}</div>
          ${state.faucet === false ? html`<div class="help">No faucet on this server. Fund the wallet from a terminal: <code>${FAUCET_HINT} ${key.address}</code></div>` : ""}
        </div>`,
      })}
      ${card({
        title: "Key",
        hint: "Kept in this browser's localStorage",
        body: html`<div class="card-body stack">
          ${kv([
            ["Address", html`<a class="hash" href="#/address/${key.address}">${shortHash(key.address, 12, 6)}</a>`],
            ["Type", "P2PKH, compressed key"],
            ["Public key", html`<span class="hash" title="${hex(key.publicKey)}">${shortHash(hex(key.publicKey), 10, 6)}</span>`],
          ])}
          ${state.revealed ? html`<div class="address-box secret"><span class="hash">${key.wif}</span>${copyButton(key.wif)}</div>
            <div class="help">Anyone with this WIF can spend the wallet's coins. Import it here or with <code>python -m wallet</code>.</div>` : ""}
          <div class="toolbar">
            <button class="btn" type="button" data-action="reveal">${state.revealed ? "Hide private key" : "Back up private key"}</button>
            <button class="btn ${state.confirmForget ? "danger" : "ghost"}" type="button" data-action="forget">${state.confirmForget ? "Click again to remove" : "Remove wallet"}</button>
          </div>
        </div>`,
      })}
    </div>
  </div>`;
}

function faucetButtons(state) {
  const disabled = state.faucet === false ? raw("disabled") : "";
  return html`<button class="btn primary" type="button" data-action="faucet" ${disabled}>Get 5 test BTC</button>
    <button class="btn" type="button" data-action="mine" ${disabled} title="Confirms everything in the mempool now">Mine a block</button>`;
}

function sendCard(state, prefill) {
  const spendable = state.coins?.spendable ?? 0n;
  return card({
    title: "Send",
    hint: "Signed here; the node verifies the signature and the coins before Core sees it",
    body: html`<form id="send" class="card-body stack" autocomplete="off">
      <div class="field"><label for="to">Pay to</label>
        <input class="input mono" id="to" spellcheck="false" placeholder="m… or n… regtest address" value="${prefill.to}"></div>
      <div class="field"><label for="amount">Amount</label>
        <div class="input-group">
          <input class="input mono" id="amount" inputmode="decimal" placeholder="0.50000000" value="${prefill.amount}">
          <span class="addon">BTC</span>
          <button class="btn" type="button" data-action="max" ${spendable > 0n ? "" : raw("disabled")}>Max</button>
        </div>
        <div class="help">Fee: 1 sat/vB, about 226 sat for one input. Change returns to this address.</div>
        <div class="form-error" role="alert" hidden></div></div>
      <div class="toolbar"><button class="btn primary" type="submit" ${spendable > 0n ? "" : raw("disabled")}>Review payment</button>
        ${spendable > 0n ? "" : html`<span class="muted">No spendable coins yet. Use the faucet.</span>`}</div>
    </form>`,
  });
}

function reviewCard(p) {
  return card({
    title: "Review payment",
    hint: "Signed in this browser, not sent yet",
    body: html`<div class="card-body stack">
      ${p.error ? html`<div class="banner bad"><div><strong>${p.error.title}</strong><span class="text">${p.error.code ? html`<code>${p.error.code}</code> · ` : ""}${p.error.message}</span></div></div>` : ""}
      ${kv([
        ["Pay to", html`<span class="hash">${p.to}</span>`],
        ["Amount", amount(p.amount)],
        ["Fee", html`${amount(p.fee)} <span class="muted">· ${number(p.size)} bytes · ${(Number(p.fee) / p.size).toFixed(2)} sat/vB</span>`],
        ["Change", p.change ? amount(p.change) : html`<span class="muted">none, the remainder is below dust and goes to the miner</span>`],
        ["Inputs", html`${p.spent.length} coin${p.spent.length === 1 ? "" : "s"}, ${amount(p.spent.reduce((n, c) => n + c.value, 0n))}`],
        ["Txid", html`<span class="hash" title="${p.txid}">${shortHash(p.txid, 12, 8)}</span>`],
      ])}
      <details class="raw-tx"><summary>Raw transaction · ${number(p.size)} bytes</summary><pre class="mono">${p.hex}</pre></details>
      <div class="toolbar">
        <button class="btn primary" type="button" data-action="broadcast">${icons.arrow} Broadcast</button>
        <button class="btn ghost" type="button" data-action="cancel">${p.error ? "Back" : "Edit"}</button>
      </div>
    </div>`,
  });
}

function coinsCard(coins) {
  return card({
    title: "Coins",
    hint: "Unspent outputs of this address in the node's UTXO set",
    body: table([
      { label: "Outpoint", render: (c) => html`<span class="hash-cell"><a class="hash" href="#/tx/${c.txid}">${shortHash(c.txid, 8, 6)}</a><span class="muted">:${c.vout}</span></span>` },
      { label: "Status", render: (c) => (c.pending ? badge("Spending", "info") : c.spendable ? badge("Spendable", "ok") : badge(`Matures at ${number(c.maturityHeight)}`, "warn")) },
      { label: "Confs", num: true, render: (c) => number(c.confirmations) },
      { label: "Value", num: true, render: (c) => amount(c.value, { trim: true }) },
    ], (coins?.coins ?? []).slice(0, 6), { empty: "No coins yet. Get some from the faucet, or have another wallet pay this address." }),
    foot: coins && coins.coins.length > 6
      ? html`<span>${coins.coins.length - 6} more</span><a href="#/address/${coins.address}">All coins of this address →</a>` : "",
  });
}

function sentCard() {
  const payments = wallet.sent();
  if (!payments.length) return "";
  return card({
    title: "Sent from this browser",
    body: table([
      { label: "Txid", render: (p) => hashLink("tx", p.txid) },
      { label: "To", render: (p) => html`<a class="hash" href="#/address/${p.to}">${shortHash(p.to, 8, 6)}</a>` },
      { label: "When", render: (p) => timeAgo(p.at) },
      { label: "Amount", num: true, render: (p) => amount(BigInt(p.amount), { trim: true }) },
    ], payments.slice(0, 8)),
  });
}

const hex = (bytes) => Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");

// ---- QR: a link that opens this page with the address filled in, so a phone camera is enough.
let qrLoader = null;
function loadQrLibrary() {
  qrLoader ??= new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "lib/qrcode-generator/qrcode.js";
    script.onload = () => resolve(window.qrcode);
    script.onerror = reject;
    document.head.append(script);
  });
  return qrLoader;
}

async function renderQr(host, address) {
  if (!host) return;
  try {
    const qrcode = await loadQrLibrary();
    const url = `${location.origin}${location.pathname}#/wallet/pay/${address}`;
    const qr = qrcode(0, "M");
    qr.addData(url);
    qr.make();
    host.innerHTML = qr.createSvgTag({ cellSize: 4, margin: 2, scalable: true, title: url });
  } catch {
    host.remove();
  }
}
