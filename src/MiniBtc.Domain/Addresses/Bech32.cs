namespace MiniBtc.Domain.Addresses;

/// <summary>
/// Encoding of SegWit addresses: Bech32 (BIP173) for witness version 0 and Bech32m (BIP350) for
/// versions 1–16. The node only displays these addresses; it verifies P2PKH spends only.
/// <code>
///   hrp | "1" | witness version (one 5-bit group) | program regrouped from 8-bit to 5-bit | 6-group checksum
/// </code>
/// The checksum is a BCH code over the hrp and the data; Bech32m differs only in the constant it
/// is XORed with, which fixes Bech32's weakness to inserted or deleted 'q's before a final 'p'.
/// </summary>
public static class Bech32
{
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private const uint Bech32Constant = 1;
    private const uint Bech32mConstant = 0x2bc830a3;

    /// <summary>A segwit address for <paramref name="program"/>, or null if version or length is out of range.</summary>
    public static string? EncodeSegwitAddress(string hrp, int witnessVersion, ReadOnlySpan<byte> program)
    {
        if (witnessVersion is < 0 or > 16 || program.Length is < 2 or > 40) return null;
        if (witnessVersion == 0 && program.Length is not (20 or 32)) return null;

        var data = new List<byte> { (byte)witnessVersion };
        data.AddRange(ConvertBits(program, 8, 5, pad: true));
        return Encode(hrp, data, witnessVersion == 0 ? Bech32Constant : Bech32mConstant);
    }

    private static string Encode(string hrp, List<byte> data, uint constant)
    {
        var values = new List<byte>(HrpExpand(hrp));
        values.AddRange(data);
        values.AddRange(new byte[6]);
        var polymod = Polymod(values) ^ constant;

        var result = new System.Text.StringBuilder(hrp.Length + 1 + data.Count + 6);
        result.Append(hrp).Append('1');
        foreach (var value in data) result.Append(Charset[value]);
        for (var i = 0; i < 6; i++) result.Append(Charset[(int)((polymod >> (5 * (5 - i))) & 31)]);
        return result.ToString();
    }

    private static uint Polymod(IEnumerable<byte> values)
    {
        uint[] generator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];
        uint checksum = 1;
        foreach (var value in values)
        {
            var top = checksum >> 25;
            checksum = ((checksum & 0x1ffffff) << 5) ^ value;
            for (var i = 0; i < 5; i++)
            {
                if (((top >> i) & 1) != 0) checksum ^= generator[i];
            }
        }
        return checksum;
    }

    private static IEnumerable<byte> HrpExpand(string hrp)
    {
        foreach (var c in hrp) yield return (byte)(c >> 5);
        yield return 0;
        foreach (var c in hrp) yield return (byte)(c & 31);
    }

    private static List<byte> ConvertBits(ReadOnlySpan<byte> data, int fromBits, int toBits, bool pad)
    {
        var result = new List<byte>();
        int accumulator = 0, bits = 0, maxValue = (1 << toBits) - 1, maxAccumulator = (1 << (fromBits + toBits - 1)) - 1;
        foreach (var value in data)
        {
            accumulator = ((accumulator << fromBits) | value) & maxAccumulator;
            bits += fromBits;
            while (bits >= toBits)
            {
                bits -= toBits;
                result.Add((byte)((accumulator >> bits) & maxValue));
            }
        }
        if (pad && bits > 0) result.Add((byte)((accumulator << (toBits - bits)) & maxValue));
        return result;
    }
}
