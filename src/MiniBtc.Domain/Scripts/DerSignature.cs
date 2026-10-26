namespace MiniBtc.Domain.Scripts;

/// <summary>
/// ECDSA signatures in scripts are DER: 30 len 02 lenR R 02 lenS S, followed by one sighash-type
/// byte. Since BIP66 (2015) only strict DER is consensus-valid. Before that, OpenSSL accepted
/// sloppy encodings that other libraries read differently, so nodes could disagree on validity.
/// </summary>
public static class DerSignature
{
    /// <summary>
    /// Core: IsValidSignatureEncoding (script/interpreter.cpp), line by line. The argument includes
    /// the trailing sighash-type byte, as in Core.
    /// </summary>
    public static bool IsValidEncoding(ReadOnlySpan<byte> sig)
    {
        // Format: 0x30 [total-length] 0x02 [R-length] [R] 0x02 [S-length] [S] [sighash]
        if (sig.Length < 9) return false;
        if (sig.Length > 73) return false;
        if (sig[0] != 0x30) return false;                                  // a compound structure
        if (sig[1] != sig.Length - 3) return false;                        // length covers the rest, minus sighash
        int lenR = sig[3];
        if (5 + lenR >= sig.Length) return false;                          // S's length byte is inside
        int lenS = sig[5 + lenR];
        if (lenR + lenS + 7 != sig.Length) return false;                   // lengths add up exactly
        if (sig[2] != 0x02) return false;                                  // R is an INTEGER
        if (lenR == 0) return false;                                       // no zero-length R
        if ((sig[4] & 0x80) != 0) return false;                            // R not negative
        if (lenR > 1 && sig[4] == 0x00 && (sig[5] & 0x80) == 0) return false;          // no needless 0x00 padding
        if (sig[lenR + 4] != 0x02) return false;                           // S is an INTEGER
        if (lenS == 0) return false;
        if ((sig[lenR + 6] & 0x80) != 0) return false;
        if (lenS > 1 && sig[lenR + 6] == 0x00 && (sig[lenR + 7] & 0x80) == 0) return false;
        return true;
    }

    /// <summary>R and S as unsigned big-endian bytes, from a signature that passed <see cref="IsValidEncoding"/>.</summary>
    public static (byte[] R, byte[] S) Parse(ReadOnlySpan<byte> sig)
    {
        int lenR = sig[3];
        int lenS = sig[5 + lenR];
        return (sig.Slice(4, lenR).ToArray(), sig.Slice(6 + lenR, lenS).ToArray());
    }
}
