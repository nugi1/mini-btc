// The node's JSON API. Every call returns parsed JSON or throws ApiError with the node's message.

export class ApiError extends Error {
  constructor(status, body) {
    super(body?.error ?? `HTTP ${status}`);
    this.status = status;
    this.body = body;
  }
}

async function request(path, options = {}) {
  const response = await fetch(path, { headers: { Accept: "application/json" }, ...options });
  const text = await response.text();
  let body = null;
  try { body = text ? JSON.parse(text) : null; } catch { body = { error: text }; }
  if (!response.ok) throw new ApiError(response.status, body);
  return body;
}

const enc = encodeURIComponent;

export const api = {
  health: () => request("/health"),
  summary: () => request("/chain/summary"),
  blocks: (limit = 20, before) => request(`/blocks?limit=${limit}${before != null ? `&before=${before}` : ""}`),
  block: (id) => request(`/blocks/${enc(id)}`),
  tx: (txid) => request(`/tx/${enc(txid)}`),
  mempool: () => request("/mempool/entries"),
  utxos: (address) => request(`/addresses/${enc(address)}/utxos`),
  balance: (address) => request(`/addresses/${enc(address)}/balance`),
  parity: (addresses = []) => request(`/parity${addresses.length ? "?" + addresses.map((a) => `address=${enc(a)}`).join("&") : ""}`),
  broadcast: (hex) => request("/tx", {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({ hex }),
  }),
};
