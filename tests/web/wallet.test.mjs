// The browser wallet's crypto and transaction code, run under Node 22+: node --test tests/web/wallet.test.mjs
// Same vectors as the Python wallet's tests: bitcoinjs-lib's signed transaction must come out byte for byte.
import assert from "node:assert/strict";
import { test } from "node:test";
import { ripemd160 } from "../../src/MiniBtc.Api/wwwroot/js/wallet/hash.js";
import { Key, derEncode, scriptForAddress } from "../../src/MiniBtc.Api/wwwroot/js/wallet/keys.js";
import { serialize, signP2pkh, txid } from "../../src/MiniBtc.Api/wwwroot/js/wallet/tx.js";
import { InsufficientFunds, buildPayment, estimatedSize } from "../../src/MiniBtc.Api/wwwroot/js/wallet/builder.js";
import { bytesToHex, hexToBytes } from "../../src/MiniBtc.Api/wwwroot/js/wallet/bytes.js";

const keyOne = () => Key.create(hexToBytes("00".repeat(31) + "01"));

test("RIPEMD-160 matches the reference vectors", () => {
  const digest = (text) => bytesToHex(ripemd160(new TextEncoder().encode(text)));
  assert.equal(digest(""), "9c1185a5c5e9fc54612808977ee8f548b2258d31");
  assert.equal(digest("abc"), "8eb208f7e05d987a9b044a8e98c6b087f15a0bfc");
  assert.equal(digest("1234567890".repeat(8)), "9b752e45573d4b39f4dbd3323cab82bf63326bfb");
});

test("key 1 derives Core's regtest address and WIF, and the WIF round-trips", async () => {
  const key = await keyOne();
  assert.equal(key.address, "mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r");
  assert.equal(key.wif, "cMahea7zqjxrtgAbB7LSGbcQUr1uX1ojuat9jZodMN87JcbXMTcA");
  assert.equal((await Key.fromWif(key.wif)).address, key.address);
});

test("signing bitcoinjs' unsigned transaction with key 1 reproduces its bytes exactly", async () => {
  const unsigned = {
    version: 1,
    inputs: [{ txid: "fe".repeat(31) + "f1", vout: 0 }],
    outputs: [{ value: 100_000n, scriptPubKey: hexToBytes("76a914c42e7ef92fdb603af844d064faad95db9bcdfd3d88ac") }],
    locktime: 0,
  };
  const key = await keyOne();
  const signed = await signP2pkh(unsigned, [key.scriptPubKey], key);
  assert.equal(bytesToHex(serialize(signed)),
    "0100000001f1fefefefefefefefefefefefefefefefefefefefefefefefefefefefefefefe000000006b"
    + "4830450221008732a460737d956fd94d49a31890b2908f7ed7025a9c1d0f25e43290f1841716022004fa7d60"
    + "8a291d44ebbbebbadaac18f943031e7de39ef3bf9920998c43e60c0401210279be667ef9dcbbac55a06295ce"
    + "870b07029bfcdb2dce28d959f2815b16f81798ffffffff01a086010000000000"
    + "1976a914c42e7ef92fdb603af844d064faad95db9bcdfd3d88ac00000000");
  assert.equal(await txid(signed), "a0ff943d3f644d8832b1fa74be4d0ad2577615dc28a7ef74ff8c271b603a082a");
});

test("a built payment matches the Python wallet's bytes for the same coins", async () => {
  const key = await keyOne();
  const coins = [
    { txid: "aa".repeat(32), vout: 0, value: 5_000_000_000n, spendable: true },
    { txid: "bb".repeat(32), vout: 1, value: 100_000n, spendable: true },
  ];
  const payment = buildPayment(coins, await scriptForAddress("mg8Jz5776UdyiYcBb9Z873NTozEiADRW5H"), 100_000_000n, key.scriptPubKey);
  assert.equal(payment.fee, BigInt(estimatedSize(1, 2)));
  const signed = await signP2pkh(payment.tx, [key.scriptPubKey], key);
  // python: build_payment + sign_p2pkh with Key.from_int(1), same coins and destination
  assert.equal(bytesToHex(serialize(signed)),
    "0200000001aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa000000006b483045022100b83fcf2458d101c6aa7e714c73bf0627e3655fe1f89bbb895fa959c1bf3c1bd30220750e9fd2ed3975aa1a6ffbdd6d6a6f1a28d4c7a94718f7c444d59295a13e259601210279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798ffffffff0200e1f505000000001976a91406afd46bcdfd22ef94ac122aa11f241244a37ecc88ac1e101024010000001976a914751e76e8199196d454941c45d1b3a323f1433bd688ac00000000");
});

test("immature coins are not spent", async () => {
  const key = await keyOne();
  const coins = [{ txid: "aa".repeat(32), vout: 0, value: 5_000_000_000n, spendable: false }];
  assert.throws(() => buildPayment(coins, key.scriptPubKey, 1_000_000n, key.scriptPubKey), InsufficientFunds);
});

test("DER keeps integers positive and minimal", () => {
  assert.equal(bytesToHex(derEncode(1n, 0x80n)), "30070201010202" + "0080");
});

test("only regtest Base58 destinations are accepted", async () => {
  await assert.rejects(scriptForAddress("bcrt1qw508d6qejxtdg4y5r3zarvary0c5xw7kygt080"));
  await assert.rejects(scriptForAddress("1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2")); // mainnet
  assert.equal(bytesToHex(await scriptForAddress("2MzQwSSnBHWHqSAqtTVQ6v47XtaisrJa1Vc")).slice(0, 4), "a914");
});
