using MiniBtc.Application.Contracts;
using MiniBtc.Domain.Utxo;
using Patterns.DDD.Application;

namespace MiniBtc.Infrastructure.Messaging;

/// <summary>
/// The one place where internal domain events become published contracts. A domain event that is
/// not mapped here never leaves the node.
/// </summary>
public static class IntegrationEvents
{
    public static void Configure(IntegrationEventMap map) =>
        map.Map((BlockConnected e) => new BlockConnectedV1(e.BlockHash.ToString(), e.Height, e.TransactionCount));
}
