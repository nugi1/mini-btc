// A regtest key: private key, compressed public key, P2PKH address and WIF, as the Python wallet
// derives them (py/wallet/keys.py). Regtest uses testnet's version bytes (Core: chainparams.cpp).
import * as secp from "../../lib/noble-secp256k1/index.js";
import * as base58 from "./base58.js";
import { hash160 } from "./hash.js";
import { concat } from "./bytes.js";

export const REGTEST = { pubkeyHash: 0x6f, scriptHash: 0xc4, secretKey: 0xef };
export const CURVE_ORDER = secp.CURVE.n;

const toBigInt = (bytes) => bytes.reduce((n, b) => n * 256n + BigInt(b), 0n);

export class InvalidKey extends Error {}

/** OP_DUP OP_HASH160 <20> OP_EQUALVERIFY OP_CHECKSIG */
export const p2pkhScript = (h160) => concat(Uint8Array.of(0x76, 0xa9, 0x14), h160, Uint8Array.of(0x88, 0xac));
/** OP_HASH160 <20> OP_EQUAL */
export const p2shScript = (h160) => concat(Uint8Array.of(0xa9, 0x14), h160, Uint8Array.of(0x87));

export class Key {
  constructor(secret) {
    const k = toBigInt(secret);
    if (secret.length !== 32 || k < 1n || k >= CURVE_ORDER) throw new InvalidKey("A private key is 32 bytes encoding a number in [1, n-1].");
    this.secret = Uint8Array.from(secret);
    this.publicKey = secp.getPublicKey(this.secret, true); // 33 bytes: 02/03 by y's parity, then x
  }

  /** 32 bytes from the browser's CSPRNG; out of range (odds about 2^-128) means draw again. */
  static generate() {
    for (;;) {
      const secret = crypto.getRandomValues(new Uint8Array(32));
      const k = toBigInt(secret);
      if (k >= 1n && k < CURVE_ORDER) return Key.create(secret);
    }
  }

  static async create(secret) {
    const key = new Key(secret);
    key.pubkeyHash = await hash160(key.publicKey);
    key.scriptPubKey = p2pkhScript(key.pubkeyHash);
    key.address = await base58.encodeCheck(concat(Uint8Array.of(REGTEST.pubkeyHash), key.pubkeyHash));
    key.wif = await base58.encodeCheck(concat(Uint8Array.of(REGTEST.secretKey), key.secret, Uint8Array.of(0x01)));
    return key;
  }

  /** Core's DecodeSecret, compressed keys only (this wallet never makes uncompressed addresses). */
  static async fromWif(wif) {
    const data = await base58.decodeCheck(wif.trim());
    if (!data || data[0] !== REGTEST.secretKey) throw new InvalidKey("Invalid private key encoding (expected a regtest/testnet WIF starting with 'c').");
    if (data.length === 33) throw new InvalidKey("Uncompressed private keys are not supported by this wallet.");
    if (data.length !== 34 || data[33] !== 0x01) throw new InvalidKey("Invalid private key encoding.");
    return Key.create(data.subarray(1, 33));
  }

  /** ECDSA over a 32-byte digest: RFC 6979 nonce, low-S (noble's default, Core's policy), DER encoded. */
  async sign(digest) {
    const signature = await secp.signAsync(digest, this.secret, { lowS: true });
    return derEncode(signature.r, signature.s);
  }
}

/**
 * Strict DER (BIP66): 30 len 02 len(r) r 02 len(s) s. Each integer is minimal big-endian, with a 00
 * prepended when its top bit is set so it doesn't read as negative.
 */
export function derEncode(r, s) {
  const int = (n) => {
    let bytes = [];
    for (; n > 0n; n >>= 8n) bytes.unshift(Number(n & 0xffn));
    if (bytes[0] & 0x80) bytes.unshift(0);
    return Uint8Array.from([0x02, bytes.length, ...bytes]);
  };
  const body = concat(int(r), int(s));
  return concat(Uint8Array.of(0x30, body.length), body);
}

/**
 * A destination's scriptPubKey: regtest P2PKH (m/n) or P2SH (2). The node's /tx gateway and Core
 * accept other outputs too, but this wallet builds only legacy ones, so other formats are refused
 * here with Core's message for an unknown address.
 */
export async function scriptForAddress(address) {
  const payload = await base58.decodeCheck(address.trim());
  if (payload?.length === 21 && payload[0] === REGTEST.pubkeyHash) return p2pkhScript(payload.subarray(1));
  if (payload?.length === 21 && payload[0] === REGTEST.scriptHash) return p2shScript(payload.subarray(1));
  throw new InvalidKey("Invalid address: this wallet pays regtest P2PKH (m…/n…) and P2SH (2…) addresses.");
}
