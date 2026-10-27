using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Scripts;
using Patterns.DDD;

namespace MiniBtc.Domain.Addresses;

/// <summary>
/// A Base58Check address: a version byte that says which script template, then a 20-byte hash.
/// It is only a human-friendly spelling of an output script, and the node indexes coins by that
/// script. P2PKH: version 0x6f on regtest, HASH160(public key). P2SH: 0xc4, HASH160(redeem script).
/// </summary>
public sealed class Address : IEquatable<Address>
{
    private Address(string text, AddressType type, byte[] hash)
    {
        Text = text;
        Type = type;
        _hash = hash;
    }

    public string Text { get; }
    public AddressType Type { get; }
    private readonly byte[] _hash;

    /// <summary>The 20-byte HASH160 the address encodes.</summary>
    public ReadOnlySpan<byte> Hash => _hash;

    /// <summary>The output script this address stands for; the node finds coins by it.</summary>
    public byte[] ScriptPubKey
    {
        get
        {
            if (Type == AddressType.P2pkh) return StandardScripts.P2pkh(_hash);
            byte[] p2sh = [0xa9, 20, .. _hash, 0x87]; // OP_HASH160 <20 bytes> OP_EQUAL
            return p2sh;
        }
    }

    public static Address FromPubKeyHash(ReadOnlySpan<byte> pubKeyHash, NetworkParams network)
    {
        if (pubKeyHash.Length != 20) throw new ArgumentException("A pubkey hash is 20 bytes.", nameof(pubKeyHash));
        return new Address(Base58.EncodeCheck([network.PubKeyAddressPrefix, .. pubKeyHash]), AddressType.P2pkh, pubKeyHash.ToArray());
    }

    /// <summary>
    /// Core's DecodeDestination for Base58 strings (src/key_io.cpp), with its error messages. Bech32
    /// (SegWit) addresses are recognized by their prefix and refused: the node verifies P2PKH only.
    /// </summary>
    public static Result<Address> Parse(string text, NetworkParams network)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.StartsWith(network.Bech32Hrp, StringComparison.OrdinalIgnoreCase))
            return new Error(AddressErrors.Code, AddressErrors.Bech32NotSupported);

        if (!Base58.TryDecodeCheck(text, 21, out var data))
        {
            return new Error(AddressErrors.Code, Base58.TryDecode(text, 100, out _)
                ? AddressErrors.InvalidChecksumOrLength
                : AddressErrors.InvalidEncoding);
        }

        AddressType? type = data.Length > 0 && data[0] == network.PubKeyAddressPrefix ? AddressType.P2pkh
            : data.Length > 0 && data[0] == network.ScriptAddressPrefix ? AddressType.P2sh
            : null;
        if (type is null)
            return new Error(AddressErrors.Code, AddressErrors.UnsupportedBase58);
        if (data.Length != 21)
            return new Error(AddressErrors.Code, AddressErrors.InvalidLength);
        return new Address(text, type.Value, data[1..]);
    }

    public bool Equals(Address? other) => other is not null && Type == other.Type && _hash.AsSpan().SequenceEqual(other._hash);
    public override bool Equals(object? obj) => Equals(obj as Address);
    public override int GetHashCode() => HashCode.Combine(Type, BitConverter.ToInt32(_hash, 0));
    public override string ToString() => Text;
}

public enum AddressType
{
    P2pkh,
    P2sh,
}

/// <summary>Bitcoin Core's own messages (src/key_io.cpp), so a client sees the same words from either node.</summary>
public static class AddressErrors
{
    public const string Code = "invalid-address";
    public const string InvalidChecksumOrLength = "Invalid checksum or length of Base58 address (P2PKH or P2SH)";
    public const string InvalidEncoding = "Invalid or unsupported Segwit (Bech32) or Base58 encoding.";
    public const string UnsupportedBase58 = "Invalid or unsupported Base58-encoded address.";
    public const string InvalidLength = "Invalid length for Base58 address (P2PKH or P2SH)";

    /// <summary>Ours: Core would decode it, but this node indexes only what it can verify.</summary>
    public const string Bech32NotSupported = "Bech32 (SegWit) addresses are not supported by mini-bitcoin; use a Base58 address";
}
