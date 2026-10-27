// One transaction: where its coins came from and where they went.
import { api } from "../api.js";
import { btc, bytes, feeRate, html, number } from "../format.js";
import * as live from "../live.js";
import { addressLink, amount, badge, card, copyButton, errorState, hashLink, icons, kv, loading, pageHead } from "../ui.js";

export async function mount(root, [txid]) {
  async function render() {
    let t;
    try {
      t = await api.tx(txid);
    } catch (error) {
      root.innerHTML = html`${pageHead({ title: "Transaction", crumbs: "Transactions" })}
        ${errorState(error.status === 404 ? "Transaction not found" : "Could not load the transaction", error.message)}`.html;
      return;
    }

    const confirmed = t.status === "confirmed";
    root.innerHTML = html`
      ${pageHead({
        crumbs: confirmed ? html`<a href="#/blocks">Blocks</a> / <a href="#/block/${t.blockHash}">${number(t.blockHeight)}</a> / Transaction`
                          : html`<a href="#/mempool">Mempool</a> / Transaction`,
        title: t.coinbase ? "Coinbase transaction" : "Transaction",
        sub: html`<span class="hash">${t.txid}</span> ${copyButton(t.txid)}`,
      })}
      <div class="banner ${confirmed ? "ok" : "info"}" style="margin-bottom:16px">
        <div><strong>${confirmed ? `Confirmed in block ${number(t.blockHeight)}` : "Unconfirmed: waiting in the mempool"}</strong>
        <span class="text">${confirmed ? `${number(t.confirmations)} confirmation${t.confirmations === 1 ? "" : "s"}. Validated by the node when it connected the block.`
                                       : "Core holds it for the next block. It confirms when a block containing it is connected."}</span></div>
      </div>
      <div class="grid two">
        ${card({ title: "Summary", body: html`<div class="card-body">${kv([
          ["Status", confirmed ? badge("Confirmed", "ok") : badge("In mempool", "info")],
          confirmed && ["Block", hashLink("block", t.blockHash)],
          ["Value moved", amount(t.totalOut)],
          ["Fee", t.coinbase ? html`<span class="muted">none (creates new coins)</span>` : t.fee == null ? html`<span class="muted">unknown</span>` : html`${amount(t.fee)} <span class="muted">· ${feeRate(t.fee, t.vsize)}</span>`],
        ])}</div>` })}
        ${card({ title: "Details", body: html`<div class="card-body">${kv([
          ["Size", html`${bytes(t.size)} <span class="muted">· ${number(t.vsize)} vB · ${number(t.weight)} WU</span>`],
          ["Version", number(t.version)],
          ["Locktime", number(t.lockTime)],
          ["wtxid", t.wtxid === t.txid ? html`<span class="muted">same as txid (no witness)</span>` : html`<span class="hash">${t.wtxid}</span>`],
        ])}</div>` })}
      </div>
      <div class="flow" style="margin-top:16px">
        ${card({ title: `Inputs (${t.inputs.length})`, hint: t.coinbase ? "" : `${btc(t.totalIn)} BTC spent`, body: html`<div class="io">${t.inputs.map(input)}</div>` })}
        <div class="flow-arrow">${icons.arrow}</div>
        ${card({ title: `Outputs (${t.outputs.length})`, hint: `${btc(t.totalOut)} BTC created`, body: html`<div class="io">${t.outputs.map(output)}</div>` })}
      </div>
      <div style="margin-top:16px">${card({ title: "Raw transaction", hint: `${number(t.size)} bytes, as serialized on the wire`,
        actions: copyButton(t.hex), body: html`<div class="card-body"><pre class="hex">${t.hex}</pre></div>` })}</div>`.html;
  }

  await render();
  // A mempool transaction becomes confirmed (or drops out) with the next block.
  const offs = [live.on("BlockConnected", render), live.on("BlockDisconnected", render)];
  return () => offs.forEach((off) => off());
}

function input(i) {
  if (i.coinbase) {
    return html`<div class="io-item"><div class="io-top"><span>${badge("Coinbase", "accent")} <span class="muted">new coins: subsidy plus fees</span></span></div>
      <details class="raw"><summary>Coinbase data</summary><pre>${i.scriptSig}</pre></details></div>`;
  }
  return html`<div class="io-item">
    <div class="io-top"><div><span class="io-index">#${i.index}</span>${i.address ? addressLink(i.address) : html`<span class="muted">${i.type ?? "unknown script"}</span>`}</div>
      <div>${i.value == null ? html`<span class="muted">unknown</span>` : amount(i.value)}</div></div>
    <div class="io-sub">Spends ${hashLink("tx", i.prevTxid)}<span>:${i.prevVout}</span>${i.type ? badge(i.type, "plain") : ""}</div>
    <details class="raw"><summary>scriptSig${i.witness.length ? " and witness" : ""}</summary>
      <pre>${i.scriptSigPushes.length ? i.scriptSigPushes.join("\n") : i.scriptSig || "(empty)"}</pre>
      ${i.witness.length ? html`<pre>${i.witness.join("\n")}</pre>` : ""}
    </details></div>`;
}

function output(o) {
  const spent = o.spent == null ? "" : o.spent ? badge("Spent", "") : badge("Unspent", "ok");
  return html`<div class="io-item">
    <div class="io-top"><div><span class="io-index">#${o.index}</span>${o.address ? addressLink(o.address) : html`<span class="muted">${o.type === "nulldata" ? "OP_RETURN data" : "no address"}</span>`}</div>
      <div>${amount(o.value)}</div></div>
    <div class="io-sub">${badge(o.type, o.type === "pubkeyhash" ? "info plain" : "plain")}${spent}</div>
    <details class="raw"><summary>scriptPubKey</summary><pre>${o.scriptPubKey}</pre></details></div>`;
}
