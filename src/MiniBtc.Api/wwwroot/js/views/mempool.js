// The node's mempool view, highest fee rate first.
import { api } from "../api.js";
import { btc, feeRate, html, number } from "../format.js";
import * as live from "../live.js";
import { amount, card, errorState, hashLink, loading, pageHead, table } from "../ui.js";

export async function mount(root) {
  root.innerHTML = html`${pageHead({
    title: "Mempool",
    sub: "Transactions Core holds for the next block, as the node tracks them: reloaded at startup, then kept current by Core's ZMQ sequence events.",
  })}<div id="list">${loading(6)}</div>`.html;

  let timer = null;
  async function render() {
    try {
      const entries = await api.mempool();
      const totalVsize = entries.reduce((sum, t) => sum + t.vsize, 0);
      const totalFees = entries.reduce((sum, t) => sum + (t.fee ?? 0), 0);
      root.querySelector("#list").innerHTML = card({
        title: `${number(entries.length)} transaction${entries.length === 1 ? "" : "s"}`,
        hint: `${number(totalVsize)} vB · ${btc(totalFees, { trim: true })} BTC in fees`,
        body: table([
          { label: "Txid", render: (t) => hashLink("tx", t.txid) },
          { label: "Fee rate", num: true, render: (t) => feeRate(t.fee, t.vsize) },
          { label: "Fee", num: true, render: (t) => (t.fee == null ? html`<span class="muted" title="An input's coin is unknown to the node">—</span>` : amount(t.fee, { trim: true })) },
          { label: "Vsize", num: true, render: (t) => `${number(t.vsize)} vB` },
          { label: "In / out", num: true, render: (t) => `${t.inputs} / ${t.outputs}` },
          { label: "Value out", num: true, render: (t) => amount(t.totalOut, { trim: true }) },
        ], entries, { empty: "The mempool is empty." }),
      }).html;
    } catch (error) {
      root.querySelector("#list").innerHTML = errorState("Mempool unavailable", error.message).html;
    }
  }
  const soon = () => { clearTimeout(timer); timer = setTimeout(render, 250); };

  await render();
  const offs = ["TxAccepted", "TxRemoved", "BlockConnected", "BlockDisconnected", "Reconnected"].map((e) => live.on(e, soon));
  return () => { offs.forEach((off) => off()); clearTimeout(timer); };
}
