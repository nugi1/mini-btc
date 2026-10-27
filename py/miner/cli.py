"""
python -m miner generate <count> --to <address>   mine blocks whose coinbase pays <address>
python -m miner tip                               Core's height and best block hash
"""

from __future__ import annotations

import argparse

from btc_rpc import CoreRpc, CoreRpcError


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="python -m miner", description="Mine regtest blocks through Bitcoin Core.")
    commands = parser.add_subparsers(dest="command", required=True)

    generate = commands.add_parser("generate", help="mine blocks paying their coinbase to an address")
    generate.add_argument("count", type=int, help="number of blocks")
    generate.add_argument("--to", required=True, metavar="ADDRESS", help="address the coinbase pays")

    commands.add_parser("tip", help="show Core's tip")

    args = parser.parse_args(argv)
    rpc = CoreRpc()
    try:
        if args.command == "generate":
            if args.count < 1:
                parser.error("count must be at least 1")
            hashes = rpc.generatetoaddress(args.count, args.to)
            height = rpc.getblockcount()
            for offset, block_hash in enumerate(hashes):
                print(f"{height - len(hashes) + 1 + offset} {block_hash}")
        else:
            print(f"{rpc.getblockcount()} {rpc.getbestblockhash()}")
    except CoreRpcError as error:
        print(f"error: {error}")
        return 1
    return 0
