namespace MiniBtc.Domain.Serialization;

/// <summary>
/// Raised when bytes are not valid Bitcoin wire format. <see cref="Reason"/> is Bitcoin Core's own
/// message for the same failure (serialize.h / streams.h), so our rejections line up with Core's.
/// At the RPC boundary Core reports any of these as "TX decode failed" or "Block decode failed".
/// </summary>
public sealed class DecodeException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

/// <summary>Core's decode failure messages, verbatim.</summary>
public static class DecodeReasons
{
    /// <summary>streams.h, DataStream::read — fewer bytes left than the field needs.</summary>
    public const string EndOfData = "DataStream::read(): end of data";

    /// <summary>serialize.h, ReadCompactSize — a value encoded in more bytes than it needs.</summary>
    public const string NonCanonicalCompactSize = "non-canonical ReadCompactSize()";

    /// <summary>serialize.h, ReadCompactSize — a length or count above MAX_SIZE (32 MiB).</summary>
    public const string CompactSizeTooLarge = "ReadCompactSize(): size too large";
}
