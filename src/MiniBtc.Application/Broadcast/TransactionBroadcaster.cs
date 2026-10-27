using MiniBtc.Application.Abstractions;
using MiniBtc.Application.Unconfirmed;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Transactions;
using Patterns.DDD;

namespace MiniBtc.Application.Broadcast;

/// <summary>
/// The broadcast gateway: a wallet hands the node a signed transaction, the node judges it with its
/// own rules against its own UTXO set and mempool view, and only a transaction it accepts goes on to
/// Core's sendrawtransaction. Core then judges it again, with its full policy. A transaction can
/// therefore be turned away twice, and the result says by whom.
/// </summary>
public sealed class TransactionBroadcaster(IBitcoinCoreRpc core, ICoinLookup coins, MempoolState mempool)
{
    public async Task<Result<BroadcastReceipt>> BroadcastAsync(string hex, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hex);
        Transaction tx;
        try
        {
            tx = Transaction.Parse(Convert.FromHexString(hex.Trim()));
        }
        catch (Exception ex) when (ex is FormatException or DecodeException)
        {
            // Core answers sendrawtransaction's decode failures with this one message (RPC -22).
            return BroadcastErrors.ByNode(BroadcastErrors.DecodeFailed, DecodeReasons.TxDecodeFailed);
        }

        // The inputs, plus the transaction's own outputs: if those exist it is already confirmed.
        var outPoints = tx.Inputs.Select(input => input.PrevOut)
            .Concat(tx.Outputs.Select((_, index) => new OutPoint(tx.Txid, (uint)index)))
            .ToHashSet();
        var snapshot = await coins.GetCoinsAsync(outPoints, cancellationToken);

        var verdict = mempool.Read(view =>
            view.Check(tx, outPoint => snapshot.Coins.GetValueOrDefault(outPoint), snapshot.TipHeight + 1));
        if (verdict.IsFailure)
            return BroadcastErrors.ByNode(verdict.Error.Code, verdict.Error.Message);

        UInt256 txid;
        try
        {
            txid = await core.SendRawTransactionAsync(Convert.ToHexStringLower(tx.ToBytes()), cancellationToken);
        }
        catch (BitcoinCoreRpcException ex) when (ex.Code is not null)
        {
            return BroadcastErrors.ByCore(ex.Code.Value, ex.CoreMessage);
        }

        // Core accepted it, so it is in Core's mempool; its ZMQ "A" event will find it here already.
        mempool.Update(view => view.Add(tx));
        return new BroadcastReceipt(txid, verdict.Value.Fee, verdict.Value.UnverifiedInputs.Count);
    }
}

/// <param name="Fee">Inputs minus outputs, in satoshis.</param>
/// <param name="UnverifiedInputs">Inputs the node could not verify (not P2PKH) and left to Core.</param>
public sealed record BroadcastReceipt(UInt256 Txid, long Fee, int UnverifiedInputs);

/// <summary>
/// A rejection's code is Core's reject reason. Rejections by Core itself carry a "core:" prefix, so
/// the caller can tell "the node refused before Core saw it" from "the node passed it, Core refused".
/// </summary>
public static class BroadcastErrors
{
    public const string CorePrefix = "core:";

    /// <summary>Ours: Core reports a decode failure with RPC code -22 and a message, no reject reason.</summary>
    public const string DecodeFailed = "tx-decode-failed";

    public static Error ByNode(string reason, string message) => new(reason, message);

    /// <summary>
    /// Core's message is "reason" or "reason, details" (TxValidationState::ToString); -27 has no
    /// reason, only "Transaction outputs already in utxo set", which maps to Core's own
    /// mempool reason for the same case.
    /// </summary>
    public static Error ByCore(int rpcCode, string message)
    {
        var reason = rpcCode switch
        {
            RpcErrorCodes.AlreadyInUtxoSet => "txn-already-known",
            RpcErrorCodes.DeserializationError => DecodeFailed,
            _ => message.Split(", ", 2)[0],
        };
        return new Error(CorePrefix + reason, $"{message} (RPC {rpcCode})");
    }

    public static bool IsFromCore(Error error) => error.Code.StartsWith(CorePrefix, StringComparison.Ordinal);

    public static string Reason(Error error) => IsFromCore(error) ? error.Code[CorePrefix.Length..] : error.Code;
}
