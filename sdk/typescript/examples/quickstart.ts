// Run with: npx tsx examples/quickstart.ts
import { AvmBalanceIndexer, ApiError, Chains } from "../src/index.js";

// Public TestNet deployment. Use AvmBalanceIndexer.voi() / .algorandMainnet(), or pass { baseUrl } for your own deployment.
const indexer = new AvmBalanceIndexer({ chain: Chains.AlgorandTestnet });

const status = await indexer.status();
console.log(`${status.chain}: ${status.state}, indexed round ${status.indexedRound} of ${status.targetRound}`);

// The five biggest holders of the native token at the latest round (amounts are decimal strings).
let shown = 0;
for await (const holder of indexer.iterateHolders("algo", 0, { limit: 100 })) {
  console.log(holder.account, BigInt(holder.amount));
  if (++shown === 5) break;
}

try {
  await indexer.token("asa", 1);
} catch (error) {
  if (error instanceof ApiError && error.isNotFound) {
    console.log("asset 1 is not known to this chain");
  } else {
    throw error;
  }
}
