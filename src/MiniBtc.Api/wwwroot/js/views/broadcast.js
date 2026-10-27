// Broadcast: hand a signed transaction to the node's gateway and see who accepts or rejects it.
import { api } from "../api.js";
import { html } from "../format.js";
import { amount, badge, card, hashLink, kv, pageHead } from "../ui.js";

export async function mount(root) {
  root.innerHTML = html`${pageHead({
    title: "Broadcast",
    sub: "POST /tx: the node checks the transaction against its own UTXO set and mempool view first, and only then hands it to Bitcoin Core.",
  })}
  <div class="grid main-side">
    ${card({ title: "Signed transaction", body: html`<form id="form" class="card-body stack">
      <div class="field"><label for="hex">Raw transaction (hex)</label>
        <textarea class="input" id="hex" spellcheck="false" placeholder="0200000001…"></textarea>
        <div class="help">The wallet prints it as <code>raw</code> with <code>python -m wallet send … --dry-run</code>.</div></div>
      <div class="toolbar"><button class="btn primary" type="submit" id="send">Broadcast</button><button class="btn ghost" type="button" id="clear">Clear</button></div>
    </form>` })}
    <div id="result">${card({ title: "Result", body: html`<div class="empty">Nothing sent yet.</div>` })}</div>
  </div>`.html;

  const form = root.querySelector("#form");
  root.querySelector("#clear").addEventListener("click", () => { root.querySelector("#hex").value = ""; });
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    const hex = root.querySelector("#hex").value.replace(/\s+/g, "");
    if (!hex) return;
    const button = root.querySelector("#send");
    button.disabled = true;
    const host = root.querySelector("#result");
    try {
      const receipt = await api.broadcast(hex);
      host.innerHTML = card({ title: "Result", body: html`<div class="card-body stack">
        <div class="banner ok"><div><strong>Accepted by the node and by Core</strong><span class="text">It is in Core's mempool and will confirm with the next block.</span></div></div>
        ${kv([["Txid", hashLink("tx", receipt.txid)], ["Fee", amount(receipt.fee)],
              ["Scripts", receipt.unverifiedInputs ? badge(`${receipt.unverifiedInputs} input(s) left to Core`, "warn") : badge("All inputs verified by the node", "ok")]])}
      </div>` }).html;
    } catch (error) {
      const body = error.body ?? {};
      const byNode = body.rejectedBy === "node";
      host.innerHTML = card({ title: "Result", body: html`<div class="card-body stack">
        <div class="banner bad"><div><strong>${body.rejectedBy ? `Rejected by ${byNode ? "the node" : "Bitcoin Core"}` : "Request failed"}</strong>
          <span class="text">${byNode ? "Core never saw it: the node's own rules refused it." : body.rejectedBy ? "The node passed it; Core's policy or consensus checks refused it." : error.message}</span></div></div>
        ${body.code ? kv([["Reject reason", html`<code>${body.code}</code>`], ["Details", body.error]]) : ""}
      </div>` }).html;
    } finally {
      button.disabled = false;
    }
  });
}
