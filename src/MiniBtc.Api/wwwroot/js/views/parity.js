// Parity: our node against Bitcoin Core, line by line.
import { api } from "../api.js";
import { clock, html, number, shortHash } from "../format.js";
import * as live from "../live.js";
import { badge, card, errorState, icons, loading, pageHead, table } from "../ui.js";

const STORE = "minibtc.parity.addresses";
const load = () => { try { return JSON.parse(localStorage.getItem(STORE)) ?? []; } catch { return []; } };
const save = (list) => { try { localStorage.setItem(STORE, JSON.stringify(list)); } catch {} };

export async function mount(root) {
  let addresses = load();

  root.innerHTML = html`${pageHead({
    title: "Parity",
    sub: "Two independent implementations, one chain. Every line compares what our node computed with what Bitcoin Core computed.",
    actions: html`<button class="btn primary" id="run" type="button">${icons.refresh} Run check</button>`,
  })}
  <div class="grid main-side">
    <div id="report">${loading(6)}</div>
    <div class="stack">
      ${card({ title: "Addresses to compare", hint: "Each is checked against Core's scantxoutset", body: html`<div class="card-body stack">
        <form id="add" class="toolbar"><input class="input" id="address" placeholder="Regtest address (m…, n…, 2…)" style="flex:1" autocomplete="off"><button class="btn" type="submit">Add</button></form>
        <div class="chips" id="chips"></div>
      </div>` })}
      ${card({ title: "What is compared", body: html`<div class="card-body"><ul style="margin:0;padding-left:18px;line-height:1.8">
        <li><strong>Tip height and hash</strong> against Core's tip</li>
        <li><strong>UTXO count and total value</strong> against <code>gettxoutsetinfo</code></li>
        <li><strong>Address balances</strong> against <code>scantxoutset</code></li>
      </ul><p class="muted" style="margin:10px 0 0">If the tips differ the node is still syncing, and the other lines may differ for that reason alone.</p></div>` })}
    </div>
  </div>`.html;

  const chips = () => {
    root.querySelector("#chips").innerHTML = addresses.length
      ? html`${addresses.map((a) => html`<span class="chip">${a}<button type="button" data-remove="${a}" aria-label="Remove">×</button></span>`)}`.html
      : `<span class="muted">No addresses yet: only the chain-wide lines are checked.</span>`;
  };

  async function run() {
    const button = root.querySelector("#run");
    button.disabled = true;
    try {
      const report = await api.parity(addresses);
      const tone = report.green ? "ok" : report.level ? "bad" : "warn";
      const title = report.green ? "All checks match" : report.level ? "Our node and Core disagree" : "Our node is behind Core";
      const text = report.green ? "Two independent implementations computed the same chain state."
        : report.level ? "Same tip, different state: one implementation has a bug. The red lines show where."
        : "The tips differ, so the node is still syncing. Run the check again in a moment.";
      root.querySelector("#report").innerHTML = card({
        title: "Result",
        hint: `Checked at ${clock(new Date(report.checkedAt))}`,
        body: html`<div class="card-body"><div class="banner ${tone}"><div><strong>${title}</strong><span class="text">${text}</span></div></div></div>
          ${table([
            { label: "Check", render: (c) => html`<strong>${c.name}</strong>` },
            { label: "Our node", render: (c) => html`<span class="mono">${fmt(c.ours)}</span>` },
            { label: "Bitcoin Core", render: (c) => html`<span class="mono">${fmt(c.core)}</span>` },
            { label: "", num: true, render: (c) => (c.match ? badge("Match", "ok") : badge("Differs", "bad")) },
          ], report.checks)}`,
      }).html;
    } catch (error) {
      root.querySelector("#report").innerHTML = errorState("Parity check failed", error.body?.error ?? error.message).html;
    } finally {
      button.disabled = false;
    }
  }

  root.querySelector("#run").addEventListener("click", run);
  root.querySelector("#add").addEventListener("submit", (event) => {
    event.preventDefault();
    const input = root.querySelector("#address");
    const value = input.value.trim();
    if (value && !addresses.includes(value)) { addresses = [...addresses, value]; save(addresses); chips(); run(); }
    input.value = "";
  });
  root.querySelector("#chips").addEventListener("click", (event) => {
    const remove = event.target.closest("[data-remove]");
    if (!remove) return;
    addresses = addresses.filter((a) => a !== remove.dataset.remove);
    save(addresses); chips(); run();
  });

  chips();
  await run();
  const offs = [live.on("BlockConnected", run), live.on("BlockDisconnected", run)];
  return () => offs.forEach((off) => off());
}

const fmt = (value) => (/^\d+$/.test(value) ? number(value) : /^[0-9a-f]{64}$/.test(value) ? shortHash(value, 12, 10) : value);
