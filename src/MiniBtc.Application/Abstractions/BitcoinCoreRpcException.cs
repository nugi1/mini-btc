namespace MiniBtc.Application.Abstractions;

/// <summary>
/// Core answered with a JSON-RPC error (<see cref="Code"/> set, Core's own code and message), or
/// the call failed at the HTTP level (<see cref="Code"/> null: wrong credentials, a proxy error).
/// </summary>
public sealed class BitcoinCoreRpcException(string method, int? code, string message)
    : Exception($"{method}: {message}" + (code is { } c ? $" (code {c})" : ""))
{
    public string Method { get; } = method;
    public int? Code { get; } = code;
    public string CoreMessage { get; } = message;
}

/// <summary>The Core error codes the node reacts to (rpc/protocol.h).</summary>
public static class RpcErrorCodes
{
    /// <summary>RPC_INVALID_ADDRESS_OR_KEY — e.g. getblock: "Block not found".</summary>
    public const int InvalidAddressOrKey = -5;

    /// <summary>RPC_INVALID_PARAMETER — e.g. getblockhash: "Block height out of range".</summary>
    public const int InvalidParameter = -8;

    /// <summary>RPC_IN_WARMUP — Core is still starting (loading the block index); retry shortly.</summary>
    public const int InWarmup = -28;
}
