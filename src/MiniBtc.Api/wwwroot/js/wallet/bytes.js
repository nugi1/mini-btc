// Byte helpers in the wire formats Bitcoin uses: hex, little-endian integers, CompactSize.

export const hexToBytes = (hex) => {
  if (hex.length % 2 || /[^0-9a-fA-F]/.test(hex)) throw new Error(`not hex: ${hex}`);
  return Uint8Array.from(hex.match(/../g) ?? [], (h) => parseInt(h, 16));
};
export const bytesToHex = (bytes) => Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");

export function concat(...parts) {
  const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let offset = 0;
  for (const p of parts) { out.set(p, offset); offset += p.length; }
  return out;
}

export const equal = (a, b) => a.length === b.length && a.every((x, i) => x === b[i]);

export function u32le(n) {
  const out = new Uint8Array(4);
  new DataView(out.buffer).setUint32(0, n, true);
  return out;
}

/** Signed 64-bit little-endian, the format of an output's value in satoshis. */
export function i64le(n) {
  const out = new Uint8Array(8);
  new DataView(out.buffer).setBigInt64(0, BigInt(n), true);
  return out;
}

/** CompactSize (Core: WriteCompactSize): 1, 3, 5 or 9 bytes. */
export function compactSize(n) {
  if (n < 0xfd) return Uint8Array.of(n);
  if (n <= 0xffff) return concat(Uint8Array.of(0xfd), u32le(n).subarray(0, 2));
  if (n <= 0xffffffff) return concat(Uint8Array.of(0xfe), u32le(n));
  return concat(Uint8Array.of(0xff), i64le(n));
}

export const varBytes = (data) => concat(compactSize(data.length), data);
