// Formatting helpers. Amounts arrive as integer satoshis and are formatted with integer arithmetic,
// never floating point, so 0.1 BTC is printed as exactly 0.10000000.

export const SATS_PER_BTC = 100_000_000n;

export function btc(sats, { trim = false } = {}) {
  if (sats == null) return "—";
  const value = BigInt(sats);
  const negative = value < 0n;
  const abs = negative ? -value : value;
  const whole = (abs / SATS_PER_BTC).toLocaleString("en-US");
  let frac = (abs % SATS_PER_BTC).toString().padStart(8, "0");
  if (trim) frac = frac.replace(/0+$/, "").padEnd(2, "0");
  return `${negative ? "-" : ""}${whole}.${frac}`;
}

export const number = (n) => (n == null ? "—" : Number(n).toLocaleString("en-US"));

export function bytes(n) {
  if (n == null) return "—";
  if (n < 1000) return `${n} B`;
  return `${(n / 1000).toLocaleString("en-US", { maximumFractionDigits: 2 })} kB`;
}

export function shortHash(hash, head = 10, tail = 8) {
  if (!hash) return "—";
  return hash.length <= head + tail + 1 ? hash : `${hash.slice(0, head)}…${hash.slice(-tail)}`;
}

export function timeAgo(iso) {
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  if (Number.isNaN(seconds)) return "—";
  if (seconds < 5) return "just now";
  if (seconds < 60) return `${seconds}s ago`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
  return `${Math.floor(seconds / 86400)}d ago`;
}

export const dateTime = (iso) =>
  new Date(iso).toLocaleString("en-GB", { year: "numeric", month: "short", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit" });

export const clock = (date = new Date()) =>
  date.toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit", second: "2-digit" });

export function feeRate(fee, vsize) {
  if (fee == null || !vsize) return "—";
  return `${(fee / vsize).toLocaleString("en-US", { maximumFractionDigits: 2 })} sat/vB`;
}

// Everything that reaches innerHTML goes through this.
export function esc(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;").replaceAll("'", "&#39;");
}

// Tagged template: interpolated values are escaped unless wrapped with raw().
const RAW = Symbol("raw");
export const raw = (html) => ({ [RAW]: true, html: String(html) });
export function html(strings, ...values) {
  let out = strings[0];
  values.forEach((value, i) => {
    const part = Array.isArray(value)
      ? value.map((v) => (v && v[RAW] ? v.html : esc(v))).join("")
      : value && value[RAW] ? value.html : esc(value);
    out += part + strings[i + 1];
  });
  return raw(out);
}
