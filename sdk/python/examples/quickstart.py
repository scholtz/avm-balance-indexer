"""Run with: python examples/quickstart.py"""
from itertools import islice

from avm_balance_indexer import ALGORAND_TESTNET, ApiError, AvmBalanceIndexer

# Public TestNet deployment. Use AvmBalanceIndexer.voi() / .algorand_mainnet(), or pass base_url= for your own deployment.
with AvmBalanceIndexer(chain=ALGORAND_TESTNET) as indexer:
    status = indexer.status()
    print(f"{status.chain}: {status.state}, indexed round {status.indexed_round} of {status.target_round}")

    # The five biggest holders of the native token at the latest round (amounts are decimal strings).
    for holder in islice(indexer.iter_holders("algo", 0, limit=100), 5):
        print(holder.account, int(holder.amount))

    try:
        indexer.token("asa", 1)
    except ApiError as error:
        if not error.is_not_found:
            raise
        print("asset 1 is not known to this chain")
