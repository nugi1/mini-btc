using System.Text;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;

namespace MiniBtc.Domain.Consensus;

/// <summary>
/// The chain-specific parameters the node needs (Core: CChainParams + Consensus::Params).
/// Only regtest is defined: Core is our only peer, so P2P values (message start, port) are left out.
/// </summary>
public sealed class NetworkParams
{
    private NetworkParams(string name, Block genesisBlock, UInt256 expectedGenesisHash, UInt256 expectedMerkleRoot)
    {
        // Core asserts the same two values when it builds its chain params. Any byte wrong in our
        // serializer or hashing would stop the node here, at startup, before it syncs a single block.
        if (genesisBlock.Hash != expectedGenesisHash || genesisBlock.Header.MerkleRoot != expectedMerkleRoot)
            throw new InvalidOperationException(
                $"{name} genesis mismatch: built {genesisBlock.Hash}, expected {expectedGenesisHash}.");

        Name = name;
        GenesisBlock = genesisBlock;
    }

    public string Name { get; }
    public Block GenesisBlock { get; }
    public UInt256 GenesisHash => GenesisBlock.Hash;

    /// <summary>The easiest target allowed; bits may never decode above it (pow.cpp DeriveTarget).</summary>
    public required UInt256 PowLimit { get; init; }

    /// <summary>Regtest never retargets: every block keeps the previous block's bits.</summary>
    public required bool PowNoRetargeting { get; init; }

    /// <summary>Blocks between subsidy halvings: 210,000 on mainnet, 150 on regtest.</summary>
    public required int SubsidyHalvingInterval { get; init; }

    /// <summary>Base58Check version byte of P2PKH addresses (regtest 111 = 0x6f: addresses start m or n).</summary>
    public required byte PubKeyAddressPrefix { get; init; }

    /// <summary>Base58Check version byte of P2SH addresses (regtest 196 = 0xc4: addresses start with 2).</summary>
    public required byte ScriptAddressPrefix { get; init; }

    /// <summary>Base58Check version byte of WIF private keys (regtest 239 = 0xef).</summary>
    public required byte SecretKeyPrefix { get; init; }

    /// <summary>Human-readable part of bech32 (SegWit) addresses.</summary>
    public required string Bech32Hrp { get; init; }

    /// <summary>
    /// Core: GetBlockSubsidy — 50 BTC, halved (right shift) once per interval, and zero once the
    /// shift would reach 64 bits. The coinbase may claim at most this plus the block's fees.
    /// </summary>
    public long GetBlockSubsidy(int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        var halvings = height / SubsidyHalvingInterval;
        return halvings >= 64 ? 0 : (50 * ConsensusConstants.Coin) >> halvings;
    }

    /// <summary>Bitcoin Core v30.0, kernel/chainparams.cpp, CRegTestParams.</summary>
    public static NetworkParams Regtest { get; } = new(
        "regtest",
        CreateGenesisBlock(time: 1296688602, nonce: 2, bits: 0x207fffff, version: 1, reward: 50 * ConsensusConstants.Coin),
        expectedGenesisHash: UInt256.Parse("0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206"),
        expectedMerkleRoot: UInt256.Parse("4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b"))
    {
        PowLimit = UInt256.Parse("7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"),
        PowNoRetargeting = true,
        SubsidyHalvingInterval = 150,
        PubKeyAddressPrefix = 111,
        ScriptAddressPrefix = 196,
        SecretKeyPrefix = 239,
        Bech32Hrp = "bcrt",
    };

    /// <summary>
    /// Core: CreateGenesisBlock. Every network shares Satoshi's coinbase. It pays 50 BTC to a fixed
    /// public key, and its scriptSig carries 486604799 (0x1d00ffff), 4, and the Times headline.
    /// Only the header's time, nonce and bits differ between networks.
    /// </summary>
    private static Block CreateGenesisBlock(uint time, uint nonce, uint bits, int version, long reward)
    {
        const string timestamp = "The Times 03/Jan/2009 Chancellor on brink of second bailout for banks";
        var genesisPubKey = Convert.FromHexString(
            "04678afdb0fe5548271967f1a67130b7105cd6a828e03909a67962e0ea1f61deb649f6bc3f4cef38c4f35504e51ec112de5c384df7ba0b8d578a4c702b6bf11d5f");

        byte[] scriptSig =
        [
            0x04, 0xff, 0xff, 0x00, 0x1d,                  // push 4 bytes: 486604799, little-endian
            0x01, 0x04,                                    // push 1 byte: CScriptNum(4)
            (byte)timestamp.Length, .. Encoding.ASCII.GetBytes(timestamp), // push 69 bytes
        ];
        byte[] scriptPubKey = [(byte)genesisPubKey.Length, .. genesisPubKey, 0xac]; // <65-byte pubkey> OP_CHECKSIG

        var coinbase = new Transaction(
            version: 1,
            inputs: [new TxIn(OutPoint.Null, scriptSig)],
            outputs: [new TxOut(reward, scriptPubKey)],
            lockTime: 0);

        var merkleRoot = MerkleTree.ComputeRoot([coinbase.Txid], out _);
        var header = new BlockHeader(version, UInt256.Zero, merkleRoot, time, bits, nonce);
        return new Block(header, [coinbase]);
    }
}
