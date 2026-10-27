// The explorer shell: hash router, global search, live status, theme, copy buttons.
import { api } from "./api.js";
import { number } from "./format.js";
import * as live from "./live.js";
import { toast } from "./ui.js";

const routes = [
  { pattern: /^\/?$/, nav: "overview", load: () => import("./views/overview.js") },
  { pattern: /^\/blocks\/?$/, nav: "blocks", load: () => import("./views/blocks.js") },
  { pattern: /^\/block\/([^/]+)$/, nav: "blocks", load: () => import("./views/block.js") },
  { pattern: /^\/tx\/([0-9a-fA-F]{64})$/, nav: "blocks", load: () => import("./views/tx.js") },
  { pattern: /^\/address\/([^/]+)$/, nav: "", load: () => import("./views/address.js") },
  { pattern: /^\/mempool\/?$/, nav: "mempool", load: () => import("./views/mempool.js") },
  { pattern: /^\/parity\/?$/, nav: "parity", load: () => import("./views/parity.js") },
  { pattern: /^\/broadcast\/?$/, nav: "broadcast", load: () => import("./views/broadcast.js") },
];

const view = document.getElementById("view");
let unmount = null;
let renderToken = 0;

async function route() {
  const path = decodeURIComponent(location.hash.replace(/^#/, "")) || "/";
  const match = routes.map((r) => ({ r, m: path.match(r.pattern) })).find((x) => x.m);
  const token = ++renderToken;

  if (unmount) { try { unmount(); } catch {} unmount = null; }
  document.querySelectorAll("#nav a").forEach((a) => a.classList.toggle("active", match && a.dataset.route === match.r.nav));

  if (!match) {
    view.innerHTML = `<div class="card"><div class="error-state"><h2>Page not found</h2><p>Nothing lives at <code>${path.replace(/[<>&]/g, "")}</code>.</p></div></div>`;
    return;
  }
  const module = await match.r.load();
  if (token !== renderToken) return; // the user navigated again while the module loaded
  view.scrollTop = 0;
  window.scrollTo(0, 0);
  unmount = (await module.mount(view, match.m.slice(1), { isCurrent: () => token === renderToken })) ?? null;
}

// ---- Global search: a height, a 64-hex id (a tx first, else a block), or an address.
document.getElementById("search").addEventListener("submit", async (event) => {
  event.preventDefault();
  const input = document.getElementById("search-input");
  const query = input.value.trim();
  if (!query) return;
  if (/^\d+$/.test(query)) { location.hash = `#/block/${query}`; }
  else if (/^[0-9a-fA-F]{64}$/.test(query)) {
    const id = query.toLowerCase();
    try { await api.tx(id); location.hash = `#/tx/${id}`; }
    catch {
      try { await api.block(id); location.hash = `#/block/${id}`; }
      catch { toast("Not found", `No transaction or block ${id} in the node's chain or mempool.`, "bad"); return; }
    }
  } else { location.hash = `#/address/${query}`; }
  input.value = "";
  input.blur();
});

// ---- Copy buttons anywhere on the page.
document.addEventListener("click", async (event) => {
  const button = event.target.closest("[data-copy]");
  if (!button) return;
  event.preventDefault();
  try { await navigator.clipboard.writeText(button.dataset.copy); toast("Copied", button.dataset.copy, "ok"); }
  catch { toast("Copy failed", "The browser refused clipboard access.", "bad"); }
});

// ---- Theme: system by default; the toggle pins light or dark.
document.getElementById("theme-toggle").addEventListener("click", () => {
  const root = document.documentElement;
  const current = root.dataset.theme ?? (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
  root.dataset.theme = current === "dark" ? "light" : "dark";
  try { localStorage.setItem("minibtc.theme", root.dataset.theme); } catch {}
});

// ---- Live status pill.
live.onState((state) => {
  const pill = document.getElementById("live-pill");
  const tone = { live: "ok live", reconnecting: "warn", connecting: "warn", offline: "bad", unavailable: "bad" }[state] ?? "";
  const label = { live: "Live", reconnecting: "Reconnecting", connecting: "Connecting", offline: "Offline", unavailable: "No live updates" }[state] ?? state;
  pill.innerHTML = `<span class="dot ${tone}"></span><span>${label}</span>`;
});

// ---- Sync pill and sidebar footer: our tip against Core's.
async function refreshStatus() {
  try {
    const s = await api.summary();
    document.getElementById("footer-network").textContent = s.network;
    document.getElementById("footer-tip").textContent = number(s.tipHeight);
    document.getElementById("footer-core").textContent = s.coreHeight == null ? "unreachable" : number(s.coreHeight);
    const pill = document.getElementById("sync-pill");
    if (s.coreHeight == null) pill.innerHTML = `<span class="dot bad"></span><span>Core unreachable</span>`;
    else if (s.coreHeight === s.tipHeight) pill.innerHTML = `<span class="dot ok"></span><span>In sync · ${number(s.tipHeight)}</span>`;
    else pill.innerHTML = `<span class="dot warn"></span><span>Syncing ${number(s.tipHeight)} / ${number(s.coreHeight)}</span>`;
  } catch {
    document.getElementById("sync-pill").innerHTML = `<span class="dot bad"></span><span>Node unreachable</span>`;
  }
}

// ---- Notifications that matter on any page.
live.on("BlockConnected", (b) => { toast(`Block ${number(b.height)} connected`, `${b.transactionCount} transaction(s) · ${b.hash}`, "accent"); refreshStatus(); });
live.on("BlockDisconnected", (b) => { toast(`Block ${number(b.height)} disconnected`, `Reorg: ${b.hash} left the chain`, "bad"); refreshStatus(); });
live.on("TxRejected", (r) => toast(`Transaction rejected by ${r.rejectedBy}`, `${r.code}${r.txid ? ` · ${r.txid}` : ""}`, "bad"));
live.on("Reconnected", refreshStatus);

window.addEventListener("hashchange", route);
refreshStatus();
setInterval(refreshStatus, 15000);
route();
live.start();
