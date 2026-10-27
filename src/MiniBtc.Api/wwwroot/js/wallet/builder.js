// Coin selection, change and fee, the same rules as py/wallet/builder.py:
// size = 10 + 148 per input + 34 per output (largest possible, so the real fee rate is never lower),
// fee = ceil(size × 1 sat/vB), change below Core's 546-sat dust threshold goes to the miner instead.

export const TX_OVERHEAD_SIZE = 10;
export const P2PKH_INPUT_SIZE = 148;
export const P2PKH_OUTPUT_SIZE = 34;
export const DUST_THRESHOLD = 546n;
export const FEE_RATE = 1n; // sat/vB

export const estimatedSize = (inputs, outputs) => TX_OVERHEAD_SIZE + P2PKH_INPUT_SIZE * inputs + P2PKH_OUTPUT_SIZE * outputs;
const feeFor = (inputs, outputs) => BigInt(estimatedSize(inputs, outputs)) * FEE_RATE;

export class InsufficientFunds extends Error {}

/**
 * coins: [{ txid, vout, value: bigint, spendable }]. Largest first, so few inputs.
 * Returns { tx, spent, fee, change } with an unsigned version-2 transaction.
 */
export function buildPayment(coins, payTo, amount, changeTo) {
  if (amount < DUST_THRESHOLD) throw new RangeError(`${amount} sat is below the dust threshold of ${DUST_THRESHOLD} sat`);
  const candidates = coins.filter((c) => c.spendable)
    .sort((a, b) => (a.value === b.value ? (a.txid === b.txid ? a.vout - b.vout : a.txid < b.txid ? -1 : 1) : a.value > b.value ? -1 : 1));
  const selected = [];
  let total = 0n;
  for (const coin of candidates) {
    selected.push(coin);
    total += coin.value;
    const change = total - amount - feeFor(selected.length, 2);
    if (change >= DUST_THRESHOLD) return payment(selected, [[amount, payTo], [change, changeTo]], total - amount - change, change);
    if (total >= amount + feeFor(selected.length, 1)) return payment(selected, [[amount, payTo]], total - amount, 0n);
  }
  const immature = coins.filter((c) => !c.spendable).reduce((n, c) => n + c.value, 0n);
  throw new InsufficientFunds(`Need ${amount} sat plus fee, but only ${total} sat is spendable${immature ? ` (${immature} sat more is immature coinbase)` : ""}.`);
}

const payment = (selected, outputs, fee, change) => ({
  tx: {
    version: 2,
    inputs: selected.map(({ txid, vout }) => ({ txid, vout })),
    outputs: outputs.map(([value, scriptPubKey]) => ({ value, scriptPubKey })),
    locktime: 0,
  },
  spent: selected,
  fee,
  change,
});
