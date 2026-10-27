namespace MiniBtc.Api.Observability;

/// <summary>
/// Every rejection is logged with the rule that caused it as its own structured field, {Rule}
/// (Core's reject reason, e.g. bad-txns-inputs-missingorspent), so logs can be filtered and counted
/// by rule. With the JSON console formatter (Logging__Console__FormatterName=json) each one is a
/// single JSON line carrying Rule, Txid and RejectedBy as properties.
/// </summary>
internal static partial class RejectionLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "Transaction {Txid} rejected by {RejectedBy}: {Rule} ({Detail})")]
    public static partial void TransactionRejected(ILogger logger, string? txid, string rejectedBy, string rule, string detail);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "Transaction {Txid} accepted by the gateway and Core (fee {Fee} sat)")]
    public static partial void TransactionAccepted(ILogger logger, string txid, long fee);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error,
        Message = "Chain sync stopped: {Rule} ({Detail})")]
    public static partial void SyncRejected(ILogger logger, string rule, string detail);
}
