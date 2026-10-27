// Base58Check (Core: src/base58.cpp): payload + first 4 bytes of HASH256(payload), in base 58.
import { hash256 } from "./hash.js";
import { concat, equal } from "./bytes.js";

const ALPHABET = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

export function encode(bytes) {
  let n = bytes.reduce((acc, b) => acc * 256n + BigInt(b), 0n);
  let out = "";
  while (n > 0n) { out = ALPHABET[Number(n % 58n)] + out; n /= 58n; }
  for (const b of bytes) { if (b !== 0) break; out = "1" + out; } // each leading zero byte is a '1'
  return out;
}

export function decode(text) {
  let n = 0n;
  for (const c of text) {
    const digit = ALPHABET.indexOf(c);
    if (digit < 0) return null;
    n = n * 58n + BigInt(digit);
  }
  const body = [];
  while (n > 0n) { body.unshift(Number(n % 256n)); n /= 256n; }
  const zeros = text.match(/^1*/)[0].length;
  return Uint8Array.from([...new Array(zeros).fill(0), ...body]);
}

export const encodeCheck = async (payload) => encode(concat(payload, (await hash256(payload)).subarray(0, 4)));

/** The payload, or null if the text isn't Base58 or the checksum doesn't match. */
export async function decodeCheck(text) {
  const data = decode(text);
  if (!data || data.length < 4) return null;
  const payload = data.subarray(0, data.length - 4);
  return equal((await hash256(payload)).subarray(0, 4), data.subarray(data.length - 4)) ? payload : null;
}
