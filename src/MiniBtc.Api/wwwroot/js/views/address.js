// One address: its balance and coins, from the node's UTXO index.
import { api } from "../api.js";
import { html, number } from "../format.js";
import * as live from "../live.js";
import { amount, badge, card, copyButton, errorState, hashLink, kv, loading, pageHead, table } from "../ui.js";

export async function mount(root, [address]) {
  async function render() {
    let u;
    try {
      u = await api.utxos(address);
    } catch (error) {
      root.innerHTML = html`${pageHead({ title: "Address", sub: address })}
        ${errorState("Not a usable address", error.body?.error ?? error.message)}`.html;
      return;
    }
    const spendable = u.utxos.filter((c) => c.spendable).reduce((s, c) => s + c.value, 0);
    const immature = u.utxos.filter((c) => !c.spendable).reduce((s, c) => s + c.value, 0);

    root.innerHTML = html`
      ${pageHead({ crumbs: "Address", title: html`<span class="hash" style="font-size:20px">${u.address}</span> ${copyButton(u.address)}`,
        sub: "No registration needed: the node indexes every coin by its output script." })}
      <div class="grid kpis">
        <div class="card kpi"><div class="kpi-label">Spendable</div><div class="kpi-value">${amount(spendable, { trim: true })}</div><div class="kpi-meta">in the next block (tip + 1)</div></div>
        <div class="card kpi"><div class="kpi-label">Immature</div><div class="kpi-value">${amount(immature, { trim: true })}</div><div class="kpi-meta">coinbase under 100 blocks deep</div></div>
        <div class="card kpi"><div class="kpi-label">Total</div><div class="kpi-value">${amount(spendable + immature, { trim: true })}</div><div class="kpi-meta">${number(u.utxos.length)} unspent output${u.utxos.length === 1 ? "" : "s"}</div></div>
        <div class="card kpi"><div class="kpi-label">As of block</div><div class="kpi-value">${number(u.tipHeight)}</div><div class="kpi-meta">the node's tip</div></div>
      </div>
      <div style="margin-top:16px">${card({ title: "Unspent outputs", hint: html`Locked by <code>${u.scriptPubKey}</code>`,
        body: table([
          { label: "Outpoint", render: (c) => html`${hashLink("tx", c.txid)}<span class="muted">:${c.vout}</span>` },
          { label: "Height", num: true, render: (c) => html`<a href="#/block/${c.height}">${number(c.height)}</a>` },
          { label: "Confirmations", num: true, render: (c) => number(c.confirmations) },
          { label: "Kind", render: (c) => (c.coinbase ? badge("Coinbase", "accent") : badge("Payment", "plain")) },
          { label: "State", render: (c) => (c.spendable ? badge("Spendable", "ok") : badge(`Matures at ${number(c.maturityHeight)}`, "warn")) },
          { label: "Value", num: true, render: (c) => amount(c.value) },
        ], [...u.utxos].reverse(), { empty: "This address holds no coins." }) })}</div>`.html;
  }

  root.innerHTML = loading(6).html;
  await render();
  const offs = [live.on("BlockConnected", render), live.on("BlockDisconnected", render)];
  return () => offs.forEach((off) => off());
}
