// One block: header fields and its transactions.
import { api } from "../api.js";
import { bytes, dateTime, feeRate, html, number, timeAgo } from "../format.js";
import { amount, badge, card, copyButton, errorState, hashLink, kv, loading, pageHead, table } from "../ui.js";

export async function mount(root, [id]) {
  root.innerHTML = loading(10).html;
  let b;
  try {
    b = await api.block(id);
  } catch (error) {
    root.innerHTML = html`${pageHead({ title: `Block ${id}`, crumbs: html`<a href="#/blocks">Blocks</a>` })}
      ${errorState(error.status === 404 ? "Block not found" : "Could not load the block", error.message)}`.html;
    return;
  }

  root.innerHTML = html`
    ${pageHead({
      crumbs: html`<a href="#/blocks">Blocks</a> / ${number(b.height)}`,
      title: `Block ${number(b.height)}`,
      sub: html`<span class="hash">${b.hash}</span> ${copyButton(b.hash)}`,
      actions: html`<div class="toolbar">
        ${b.height > 0 ? html`<a class="btn" href="#/block/${b.height - 1}">← ${number(b.height - 1)}</a>` : ""}
        ${b.nextHash ? html`<a class="btn" href="#/block/${b.nextHash}">${number(b.height + 1)} →</a>` : ""}
      </div>`,
    })}
    <div class="grid two">
      ${card({ title: "Summary", body: html`<div class="card-body">${kv([
        ["Status", html`${badge("In the active chain", "ok")} ${badge(`${number(b.confirmations)} confirmation${b.confirmations === 1 ? "" : "s"}`, "info")}`],
        ["Mined", html`${dateTime(b.time)} <span class="muted">(${timeAgo(b.time)})</span>`],
        ["Transactions", number(b.transactions.length)],
        ["Size", html`${bytes(b.size)} <span class="muted">· weight ${number(b.weight)} WU</span>`],
        ["Subsidy", amount(b.subsidy)],
        ["Fees", amount(b.fees)],
      ])}</div>` })}
      ${card({ title: "Header", hint: "The 80 bytes the proof of work commits to", body: html`<div class="card-body">${kv([
        ["Previous block", b.height > 0 ? hashLink("block", b.previousHash) : html`<span class="muted">none (genesis)</span>`],
        ["Merkle root", html`<span class="hash">${b.merkleRoot}</span>`],
        ["Version", html`<code>0x${b.version}</code>`],
        ["Bits", html`<code>0x${b.bits}</code>`],
        ["Nonce", number(b.nonce)],
      ])}</div>` })}
    </div>
    <div style="margin-top:16px">${card({
      title: "Transactions",
      hint: "Decoded by the node from the stored block bytes",
      body: table([
        { label: "Txid", render: (t) => html`${hashLink("tx", t.txid)} ${t.coinbase ? badge("Coinbase", "accent") : ""}` },
        { label: "Inputs", num: true, render: (t) => (t.coinbase ? "—" : number(t.inputs)) },
        { label: "Outputs", num: true, render: (t) => number(t.outputs) },
        { label: "Value out", num: true, render: (t) => amount(t.totalOut, { trim: true }) },
        { label: "Fee", num: true, render: (t) => (t.fee == null ? html`<span class="muted">—</span>` : amount(t.fee, { trim: true })) },
        { label: "Fee rate", num: true, render: (t) => feeRate(t.fee, t.vsize) },
      ], b.transactions),
    })}</div>`.html;
}
