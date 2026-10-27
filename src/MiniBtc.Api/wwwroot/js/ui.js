// Small render helpers shared by every view. Each returns an html`` fragment.
import { btc, esc, html, raw, shortHash } from "./format.js";

export const icons = {
  copy: raw('<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>'),
  block: raw('<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/></svg>'),
  tx: raw('<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M7 7h11l-3-3M17 17H6l3 3"/></svg>'),
  undo: raw('<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 7v6h6"/><path d="M21 17a9 9 0 0 0-15-6.7L3 13"/></svg>'),
  x: raw('<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 6 6 18M6 6l12 12"/></svg>'),
  arrow: raw('<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M5 12h14M13 6l6 6-6 6"/></svg>'),
  check: raw('<svg class="check-mark" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4"><path d="m5 12 5 5 9-10"/></svg>'),
  refresh: raw('<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 12a9 9 0 1 1-2.6-6.4L21 8"/><path d="M21 3v5h-5"/></svg>'),
};

export const copyButton = (value) =>
  html`<button class="copy" type="button" data-copy="${value}" title="Copy" aria-label="Copy">${icons.copy}</button>`;

/** A hash rendered as a link to its page, shortened unless full, with a copy button. */
export function hashLink(kind, hash, { full = false } = {}) {
  if (!hash) return html`<span class="muted">—</span>`;
  const label = full ? hash : shortHash(hash);
  return html`<span class="hash-cell"><a class="hash" href="#/${kind}/${hash}" title="${hash}">${label}</a>${copyButton(hash)}</span>`;
}

export function addressLink(address) {
  if (!address) return html`<span class="muted">—</span>`;
  return html`<span class="hash-cell"><a class="hash" href="#/address/${address}">${address}</a>${copyButton(address)}</span>`;
}

export const amount = (sats, opts) => html`<span class="amount">${btc(sats, opts)}<span class="unit">BTC</span></span>`;

export const badge = (text, tone = "") => html`<span class="badge ${tone}">${text}</span>`;

export function card({ title, hint = "", actions = "", body, foot = "", id = "" }) {
  return html`<section class="card" ${raw(id ? `id="${esc(id)}"` : "")}>
    ${title ? html`<div class="card-head"><div><h2>${title}</h2>${hint ? html`<div class="hint">${hint}</div>` : ""}</div>${actions}</div>` : ""}
    ${body}
    ${foot ? html`<div class="card-foot">${foot}</div>` : ""}
  </section>`;
}

/**
 * columns: [{ label, num?, render(row) }]. rows: array. Empty rows show `empty`.
 */
export function table(columns, rows, { empty = "Nothing here yet.", rowAttrs = () => "" } = {}) {
  if (!rows.length) return html`<div class="empty">${empty}</div>`;
  return html`<div class="table-wrap"><table class="data">
    <thead><tr>${columns.map((c) => html`<th class="${c.num ? "num" : ""}">${c.label}</th>`)}</tr></thead>
    <tbody>${rows.map((row) => html`<tr ${raw(rowAttrs(row))}>${columns.map((c) => html`<td class="${c.num ? "num" : ""}">${c.render(row)}</td>`)}</tr>`)}</tbody>
  </table></div>`;
}

export const kv = (pairs) =>
  html`<div class="kv">${pairs.filter(Boolean).map(([k, v]) => html`<div class="k">${k}</div><div class="v">${v}</div>`)}</div>`;

export const loading = (lines = 4) =>
  html`<div class="card"><div class="card-body stack">${Array.from({ length: lines }, (_, i) => html`<div class="skeleton" style="width:${90 - i * 12}%"></div>`)}</div></div>`;

export const errorState = (title, message) =>
  html`<div class="card"><div class="error-state"><h2>${title}</h2><p>${message}</p></div></div>`;

export function pageHead({ title, sub = "", crumbs = "", actions = "" }) {
  return html`<div class="page-head"><div>
      ${crumbs ? html`<div class="crumbs">${crumbs}</div>` : ""}
      <h1>${title}</h1>
      ${sub ? html`<div class="sub">${sub}</div>` : ""}
    </div>${actions}</div>`;
}

export function toast(title, detail = "", tone = "") {
  const host = document.getElementById("toasts");
  const node = document.createElement("div");
  node.className = `toast ${tone}`;
  node.innerHTML = html`<div class="title">${title}</div>${detail ? html`<div class="detail">${detail}</div>` : ""}`.html;
  host.append(node);
  while (host.children.length > 4) host.firstElementChild.remove();
  setTimeout(() => node.remove(), 6000);
}
