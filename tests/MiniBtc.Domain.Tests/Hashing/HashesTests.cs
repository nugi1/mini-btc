using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Tests.TestData;

namespace MiniBtc.Domain.Tests.Hashing;

public class HashesTests
{
    [Fact]
    public void Hash256_of_the_regtest_genesis_header_is_the_genesis_hash()
    {
        Assert.Equal(RegtestGenesis.Hash, Hashes.Hash256(RegtestGenesis.Header).ToString());
    }

    [Fact]
    public void Hash256_of_the_genesis_coinbase_is_its_txid_and_the_merkle_root()
    {
        Assert.Equal(RegtestGenesis.MerkleRoot, Hashes.Hash256(RegtestGenesis.Coinbase).ToString());
    }

    [Fact]
    public void Hash256_of_nothing_is_sha256_applied_twice()
    {
        // Wire order here: this is the raw digest, not a txid or block hash shown to people.
        Assert.Equal(
            "5df6e0e2761359d30a8275058e299fcc0381534545f55cf43e41983f5d4c9456",
            Convert.ToHexStringLower(Hashes.Hash256([]).ToWireBytes()));
    }

    // Reference vectors from the RIPEMD-160 paper (Dobbertin, Bosselaers, Preneel).
    [Theory]
    [InlineData("", "9c1185a5c5e9fc54612808977ee8f548b2258d31")]
    [InlineData("abc", "8eb208f7e05d987a9b044a8e98c6b087f15a0bfc")]
    public void Ripemd160_matches_the_reference_vectors(string message, string expected)
    {
        Assert.Equal(expected, Convert.ToHexStringLower(Hashes.Ripemd160(System.Text.Encoding.ASCII.GetBytes(message))));
    }

    [Fact]
    public void Hash160_of_the_generator_point_matches_bip173()
    {
        // Compressed public key of private key 1 (the secp256k1 generator G). BIP173 uses its
        // Hash160 as the witness program of bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4.
        var pubKey = Convert.FromHexString("0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798");

        var hash = Hashes.Hash160(pubKey);

        Assert.Equal(20, hash.Length);
        Assert.Equal("751e76e8199196d454941c45d1b3a323f1433bd6", Convert.ToHexStringLower(hash));
    }
}
