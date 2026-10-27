// SHA-256 from the browser (Web Crypto, which needs HTTPS or localhost), RIPEMD-160 in plain JS.

export async function sha256(bytes) {
  return new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
}

export const hash256 = async (bytes) => sha256(await sha256(bytes));
export const hash160 = async (bytes) => ripemd160(await sha256(bytes));

// RIPEMD-160, ported line for line from Bitcoin Core's test_framework/crypto/ripemd160.py
// (the same reference the Python wallet uses). Web Crypto doesn't offer RIPEMD-160.
const ML = [0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,7,4,13,1,10,6,15,3,12,0,9,5,2,14,11,8,3,10,14,4,9,15,8,1,2,7,0,6,13,11,5,12,1,9,11,10,0,8,12,4,13,3,7,15,14,5,6,2,4,0,5,9,7,12,2,10,14,1,3,8,11,6,15,13];
const MR = [5,14,7,0,9,2,11,4,13,6,15,8,1,10,3,12,6,11,3,7,0,13,5,10,14,15,8,12,4,9,1,2,15,5,1,3,7,14,6,9,11,8,12,2,10,0,4,13,8,6,4,1,3,11,15,0,5,12,2,13,9,7,10,14,12,15,10,4,1,5,8,7,6,2,13,14,0,3,9,11];
const RL = [11,14,15,12,5,8,7,9,11,13,14,15,6,7,9,8,7,6,8,13,11,9,7,15,7,12,15,9,11,7,13,12,11,13,6,7,14,9,13,15,14,8,13,6,5,12,7,5,11,12,14,15,14,15,9,8,9,14,5,6,8,6,5,12,9,15,5,11,6,8,13,12,5,12,13,14,11,8,5,6];
const RR = [8,9,9,11,13,15,15,5,7,7,8,11,14,14,12,6,9,13,15,7,12,8,9,11,7,7,12,7,6,15,13,11,9,7,15,11,8,6,6,14,12,13,5,14,13,13,7,5,15,5,8,11,14,14,6,14,6,9,12,9,12,5,15,8,8,5,12,9,12,5,14,6,8,13,6,5,15,13,11,11];
const KL = [0, 0x5a827999, 0x6ed9eba1, 0x8f1bbcdc, 0xa953fd4e];
const KR = [0x50a28be6, 0x5c4dd124, 0x6d703ef3, 0x7a6d76e9, 0];

const fi = (x, y, z, i) =>
  i === 0 ? x ^ y ^ z : i === 1 ? (x & y) | (~x & z) : i === 2 ? (x | ~y) ^ z : i === 3 ? (x & z) | (y & ~z) : x ^ (y | ~z);
const rol = (x, i) => ((x << i) | (x >>> (32 - i))) >>> 0;

function compress(h, block) {
  const x = Array.from({ length: 16 }, (_, i) => block[4 * i] | (block[4 * i + 1] << 8) | (block[4 * i + 2] << 16) | (block[4 * i + 3] << 24));
  let [al, bl, cl, dl, el] = h, [ar, br, cr, dr, er] = h;
  for (let j = 0; j < 80; j++) {
    const rnd = j >> 4;
    al = (rol((al + fi(bl, cl, dl, rnd) + x[ML[j]] + KL[rnd]) >>> 0, RL[j]) + el) >>> 0;
    [al, bl, cl, dl, el] = [el, al, bl, rol(cl, 10), dl];
    ar = (rol((ar + fi(br, cr, dr, 4 - rnd) + x[MR[j]] + KR[rnd]) >>> 0, RR[j]) + er) >>> 0;
    [ar, br, cr, dr, er] = [er, ar, br, rol(cr, 10), dr];
  }
  return [(h[1] + cl + dr) >>> 0, (h[2] + dl + er) >>> 0, (h[3] + el + ar) >>> 0, (h[4] + al + br) >>> 0, (h[0] + bl + cr) >>> 0];
}

export function ripemd160(data) {
  let h = [0x67452301, 0xefcdab89, 0x98badcfe, 0x10325476, 0xc3d2e1f0];
  const full = data.length >> 6;
  for (let b = 0; b < full; b++) h = compress(h, data.subarray(64 * b, 64 * b + 64));
  const padLength = (119 - data.length) & 63;
  const fin = new Uint8Array(data.length - 64 * full + 1 + padLength + 8);
  fin.set(data.subarray(64 * full));
  fin[data.length - 64 * full] = 0x80;
  const bits = BigInt(data.length) * 8n;
  for (let i = 0; i < 8; i++) fin[fin.length - 8 + i] = Number((bits >> BigInt(8 * i)) & 0xffn);
  for (let b = 0; b < fin.length >> 6; b++) h = compress(h, fin.subarray(64 * b, 64 * b + 64));
  const out = new Uint8Array(20);
  h.forEach((word, i) => { for (let k = 0; k < 4; k++) out[4 * i + k] = (word >>> (8 * k)) & 0xff; });
  return out;
}
