// The browser wallet's state: one key kept in this browser's localStorage, its coins from the node,
// and the outpoints our own unconfirmed payments spent. The node never sees the key: it receives
// signed transactions only, the same as from the Python wallet.
import { api } from "../api.js";
import { Key, scriptForAddress } from "./keys.js";
import { buildPayment } from "./builder.js";
import { bytesToHex } from "./bytes.js";
import { serialize, signP2pkh, txid } from "./tx.js";

const STORAGE = { wif: "minibtc.wallet.wif", sent: "minibtc.wallet.sent", pending: "minibtc.wallet.pending" };

const read = (name, fallback) => { try { return JSON.parse(localStorage.getItem(name)) ?? fallback; } catch { return fallback; } };
const write = (name, value) => { try { localStorage.setItem(name, JSON.stringify(value)); return true; } catch { return false; } };
const remove = (name) => { try { localStorage.removeItem(name); } catch {} };

/** Web Crypto's SHA-256 exists only in a secure context: HTTPS, or http://localhost. */
export const supported = () => Boolean(globalThis.isSecureContext && crypto?.subtle);

export async function load() {
  const wif = read(STORAGE.wif, null);
  return wif ? Key.fromWif(wif) : null;
}

/** Keeps the key; false if the browser refused storage (private mode), so the page can warn. */
export const save = (key) => write(STORAGE.wif, key.wif);

export function forget() {
  Object.values(STORAGE).forEach(remove);
}

/** Payments sent from this browser, newest first: [{ txid, to, amount, fee, at }]. */
export const sent = () => read(STORAGE.sent, []);

/**
 * The node's view of our coins. Its UTXO set is confirmed coins only, so a coin our own payment
 * just spent stays in it until the next block; those are hidden here until the tip moves.
 */
export async function coins(key) {
  const reply = await api.utxos(key.address);
  const pending = read(STORAGE.pending, { tip: null, outpoints: [] });
  const hidden = new Set(pending.tip === reply.tipHeight ? pending.outpoints : []);
  if (pending.tip !== reply.tipHeight && pending.outpoints.length) write(STORAGE.pending, { tip: reply.tipHeight, outpoints: [] });
  const all = reply.utxos.map((u) => ({ ...u, value: BigInt(u.value), pending: hidden.has(`${u.txid}:${u.vout}`) }));
  const sum = (list) => list.reduce((n, c) => n + c.value, 0n);
  return {
    address: reply.address,
    tipHeight: reply.tipHeight,
    coins: all,
    spendable: sum(all.filter((c) => c.spendable && !c.pending)),
    immature: sum(all.filter((c) => !c.spendable)),
    pending: sum(all.filter((c) => c.pending)),
  };
}

/** Builds and signs; nothing leaves the browser yet. amount in satoshis (bigint). */
export async function prepare(key, state, to, amount) {
  const payTo = await scriptForAddress(to);
  const usable = state.coins.filter((c) => !c.pending).map((c) => ({ ...c, spendable: c.spendable }));
  const payment = buildPayment(usable, payTo, amount, key.scriptPubKey);
  const signed = await signP2pkh(payment.tx, payment.spent.map(() => key.scriptPubKey), key);
  const raw = serialize(signed);
  return { ...payment, to: to.trim(), amount, hex: bytesToHex(raw), size: raw.length, txid: await txid(signed) };
}

/** POST /tx. On success the spent coins are hidden until the next block, and the payment is logged. */
export async function broadcast(prepared, tipHeight) {
  const receipt = await api.broadcast(prepared.hex);
  const pending = read(STORAGE.pending, { tip: tipHeight, outpoints: [] });
  const outpoints = (pending.tip === tipHeight ? pending.outpoints : []).concat(prepared.spent.map((c) => `${c.txid}:${c.vout}`));
  write(STORAGE.pending, { tip: tipHeight, outpoints });
  write(STORAGE.sent, [{ txid: receipt.txid, to: prepared.to, amount: prepared.amount.toString(), fee: prepared.fee.toString(), at: new Date().toISOString() }, ...sent()].slice(0, 20));
  return receipt;
}

/** "1.5" → 150000000n, exactly; at most 8 decimals. Null if it isn't an amount. */
export function parseBtc(text) {
  const match = String(text).trim().match(/^(\d{0,8})(?:\.(\d{0,8}))?$/);
  if (!match || (!match[1] && !match[2])) return null;
  return BigInt(match[1] || "0") * 100_000_000n + BigInt((match[2] ?? "").padEnd(8, "0"));
}

export { Key };
