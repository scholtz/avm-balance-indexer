# Verified ledger facts and test oracles

Established while building native-token indexing (issue #13, PR #21). Check these before changing normalization,
hashing or the live tests.

- The rewards pool is debited every block by `rate + prevResidue - residue` (from headers only); protocols before v31
  use the previous rate in the recalculation round. TestNet has hotfix transfers at rounds 1499995 and 2926564
  (bank to pool, 20,000,000,000).
- The Algorand5 SDK block model drops apply data (rs, rr, rc, ca) and `GossipHttpClient` ignores HTTP status, so the repo
  fetches raw bytes itself and reads apply data with a msgpack walk.
- The genesis hash reproduces from the served genesis JSON: sorted keys, minimal ints, base64 key material as bin,
  prefix "GE", SHA-512/256, keep every key present in the JSON (MainNet needs its explicit empty comments).
- Block hash = SHA-512/256("BH" + canonical block map without `txns`) and equals the certificate digest.
- Public oracle: `GET {nodely algod}/v2/deltas/{round}` gives exact post-round balances for TestNet and MainNet (not
  Voi, which prunes). Indexer `round=` rewind is disabled on free endpoints.
- MainNet's first 1000 rounds have no transactions (only pool movement); TestNet has 28; Voi only zero-impact ones.
- Proposer payout realizes pending rewards invisibly (Move with nil pointers). `PayoutEraRoundsTests` checks 100 rounds
  ~5000 behind the tip of MainNet (172 of 200 blocks paid a proposer) and TestNet against `/v2/deltas`: no discrepancy, no other
  implicit movement (suspension, bonus) found. The theoretical gap (an old online proposer with unrealized rewards) is untested.
- Recent `/v2/deltas` documents contain integer map keys (invalid JSON) after the leading `Accts` object; parse only that part. Payset commitment
  and certificate are unverified (issue #23).
