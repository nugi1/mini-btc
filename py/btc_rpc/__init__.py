"""Bitcoin Core JSON-RPC client shared by the wallet and the miner."""

from btc_rpc.client import CoreRpc, CoreRpcError, RpcErrorCode

__all__ = ["CoreRpc", "CoreRpcError", "RpcErrorCode"]
