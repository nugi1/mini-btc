// Legacy transactions: serialization, the SIGHASH_ALL digest and P2PKH signing. A line-for-line
// counterpart of py/wallet/tx.py and py/wallet/signing.py; see those for the byte layouts.
import { hash256 } from "./hash.js";
import { bytesToHex, compactSize, concat, equal, hexToBytes, i64le, u32le, varBytes } from "./bytes.js";

export const SIGHASH_ALL = 0x01;
export const FINAL_SEQUENCE = 0xffffffff;

/** txid in display order → the 36-byte outpoint on the wire (txid reversed, then index LE). */
const outpoint = ({ txid, vout }) => concat(hexToBytes(txid).reverse(), u32le(vout));

const serializeInput = (input, scriptSig) => concat(outpoint(input), varBytes(scriptSig), u32le(input.sequence ?? FINAL_SEQUENCE));
const serializeOutput = (output) => concat(i64le(output.value), varBytes(output.scriptPubKey));

/** tx = { version, inputs: [{ txid, vout, scriptSig?, sequence? }], outputs: [{ value, scriptPubKey }], locktime } */
export function serialize(tx, scriptSigFor = (input) => input.scriptSig ?? new Uint8Array()) {
  return concat(
    u32le(tx.version ?? 2),
    compactSize(tx.inputs.length), ...tx.inputs.map((input, i) => serializeInput(input, scriptSigFor(input, i))),
    compactSize(tx.outputs.length), ...tx.outputs.map(serializeOutput),
    u32le(tx.locktime ?? 0),
  );
}

export const txid = async (tx) => bytesToHex((await hash256(serialize(tx))).reverse());

/** Every scriptSig emptied except input `index`, which holds the spent script; hash type appended. */
export async function legacySighashAll(tx, index, scriptCode) {
  if (index < 0 || index >= tx.inputs.length) throw new RangeError("input index out of range");
  const preimage = concat(serialize(tx, (_, i) => (i === index ? scriptCode : new Uint8Array())), u32le(SIGHASH_ALL));
  return hash256(preimage);
}

const push = (data) => { // the wallet only pushes 33..72 bytes: a direct push opcode is the length
  if (data.length > 75) throw new RangeError("push too long for a direct push opcode");
  return concat(Uint8Array.of(data.length), data);
};

/** Signs every input with `key`; spentScripts[i] is the scriptPubKey input i spends. Returns a new tx. */
export async function signP2pkh(tx, spentScripts, key) {
  if (spentScripts.length !== tx.inputs.length) throw new Error("one spent script per input");
  const scriptSigs = [];
  for (const [index, spent] of spentScripts.entries()) {
    if (!equal(spent, key.scriptPubKey)) throw new Error(`input ${index} spends a script this key can't sign`);
    const signature = await key.sign(await legacySighashAll(tx, index, spent));
    scriptSigs.push(concat(push(concat(signature, Uint8Array.of(SIGHASH_ALL))), push(key.publicKey)));
  }
  return { ...tx, inputs: tx.inputs.map((input, i) => ({ ...input, scriptSig: scriptSigs[i] })) };
}
