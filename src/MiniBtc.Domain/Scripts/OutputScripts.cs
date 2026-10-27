using MiniBtc.Domain.Addresses;
using MiniBtc.Domain.Consensus;

namespace MiniBtc.Domain.Scripts;

/// <summary>The output script templates Core recognizes (script/solver.h TxoutType).</summary>
public enum OutputType
{
    NonStandard,
    Anchor,
    PubKey,
    PubKeyHash,
    ScriptHash,
    Multisig,
    NullData,
    WitnessV0KeyHash,
    WitnessV0ScriptHash,
    WitnessV1Taproot,
    WitnessUnknown,
}

/// <summary>
/// Classifies an output script the way Core's Solver does, in its order, and spells its address the
/// way Core's EncodeDestination does. For display: the node's consensus code never needs either.
/// </summary>
public static class OutputScripts
{
    private const byte OpReturn = 0x6a;
    private const byte OpCheckSig = 0xac;
    private const byte OpCheckMultisig = 0xae;

    public static OutputType Classify(ReadOnlySpan<byte> script)
    {
        if (script.Length == 23 && script[0] == 0xa9 && script[1] == 20 && script[22] == 0x87)
            return OutputType.ScriptHash;

        if (TryGetWitnessProgram(script, out var version, out var program))
        {
            if (version == 0 && program.Length == 20) return OutputType.WitnessV0KeyHash;
            if (version == 0 && program.Length == 32) return OutputType.WitnessV0ScriptHash;
            if (version == 1 && program.Length == 32) return OutputType.WitnessV1Taproot;
            if (version == 1 && program is [0x4e, 0x73]) return OutputType.Anchor;
            if (version != 0) return OutputType.WitnessUnknown;
            return OutputType.NonStandard;
        }

        if (script.Length >= 1 && script[0] == OpReturn && IsPushOnly(script[1..]))
            return OutputType.NullData;

        if ((script.Length == 35 && script[0] == 33 || script.Length == 67 && script[0] == 65) && script[^1] == OpCheckSig)
            return OutputType.PubKey;

        if (StandardScripts.TryGetP2pkhHash(script, out _))
            return OutputType.PubKeyHash;

        return IsMultisig(script) ? OutputType.Multisig : OutputType.NonStandard;
    }

    /// <summary>Core's names, as getblock and decoderawtransaction print them.</summary>
    public static string CoreName(OutputType type) => type switch
    {
        OutputType.Anchor => "anchor",
        OutputType.PubKey => "pubkey",
        OutputType.PubKeyHash => "pubkeyhash",
        OutputType.ScriptHash => "scripthash",
        OutputType.Multisig => "multisig",
        OutputType.NullData => "nulldata",
        OutputType.WitnessV0KeyHash => "witness_v0_keyhash",
        OutputType.WitnessV0ScriptHash => "witness_v0_scripthash",
        OutputType.WitnessV1Taproot => "witness_v1_taproot",
        OutputType.WitnessUnknown => "witness_unknown",
        _ => "nonstandard",
    };

    /// <summary>The address for the script, or null for types that have none (pubkey, multisig, nulldata, nonstandard).</summary>
    public static string? ToAddress(ReadOnlySpan<byte> script, NetworkParams network)
    {
        switch (Classify(script))
        {
            case OutputType.PubKeyHash:
                return Base58.EncodeCheck([network.PubKeyAddressPrefix, .. script[3..23]]);
            case OutputType.ScriptHash:
                return Base58.EncodeCheck([network.ScriptAddressPrefix, .. script[2..22]]);
            case OutputType.WitnessV0KeyHash or OutputType.WitnessV0ScriptHash or OutputType.WitnessV1Taproot
                or OutputType.Anchor or OutputType.WitnessUnknown:
                TryGetWitnessProgram(script, out var version, out var program);
                return Bech32.EncodeSegwitAddress(network.Bech32Hrp, version, program);
            default:
                return null;
        }
    }

    /// <summary>Core's IsWitnessProgram: OP_0 or OP_1..OP_16, then one push of 2–40 bytes, nothing else.</summary>
    public static bool TryGetWitnessProgram(ReadOnlySpan<byte> script, out int version, out byte[] program)
    {
        version = -1;
        program = [];
        if (script.Length is < 4 or > 42) return false;
        if (script[0] != 0x00 && script[0] is < 0x51 or > 0x60) return false;
        if (script[1] + 2 != script.Length) return false;
        version = script[0] == 0x00 ? 0 : script[0] - 0x50;
        program = script[2..].ToArray();
        return true;
    }

    /// <summary>Core's IsPushOnly: only pushes and OP_1NEGATE..OP_16, and every push must be complete.</summary>
    private static bool IsPushOnly(ReadOnlySpan<byte> script)
    {
        var position = 0;
        while (position < script.Length)
        {
            var opcode = script[position++];
            if (opcode > 0x60) return false;
            int length;
            if (opcode < 0x4c) length = opcode;
            else if (opcode == 0x4c && position + 1 <= script.Length) { length = script[position]; position += 1; }
            else if (opcode == 0x4d && position + 2 <= script.Length) { length = script[position] | script[position + 1] << 8; position += 2; }
            else if (opcode == 0x4e && position + 4 <= script.Length)
            {
                var declared = (uint)(script[position] | script[position + 1] << 8 | script[position + 2] << 16 | script[position + 3] << 24);
                if (declared > int.MaxValue) return false;
                length = (int)declared;
                position += 4;
            }
            else if (opcode >= 0x4f) length = 0; // OP_1NEGATE, OP_RESERVED, OP_1..OP_16: no data
            else return false;
            if (length > script.Length - position) return false;
            position += length;
        }
        return true;
    }

    /// <summary>OP_m &lt;pubkey&gt;… OP_n OP_CHECKMULTISIG, 1 ≤ m ≤ n ≤ 16, keys of 33 or 65 bytes.</summary>
    private static bool IsMultisig(ReadOnlySpan<byte> script)
    {
        if (script.Length < 3 || script[^1] != OpCheckMultisig) return false;
        int required = script[0] - 0x50, total = script[^2] - 0x50;
        if (required is < 1 or > 16 || total is < 1 or > 16 || required > total) return false;
        var position = 1;
        for (var key = 0; key < total; key++)
        {
            if (position >= script.Length - 2) return false;
            var length = script[position];
            if (length is not (33 or 65) || position + 1 + length > script.Length - 2) return false;
            position += 1 + length;
        }
        return position == script.Length - 2;
    }
}
