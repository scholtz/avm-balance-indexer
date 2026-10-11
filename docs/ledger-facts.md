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
- Proposer payout realizes pending rewards invisibly (Move with nil pointers). `PayoutEraRoundsTests` compares recent
  MainNet and TestNet rounds with `/v2/deltas` (relative check between two touches of an account, so a proposer's first
  appearance in the window is not checked): no discrepancy found. Suspension, bonus and the old-proposer-with-unrealized-rewards
  case are not exercised.
- Recent `/v2/deltas` documents contain integer map keys (invalid JSON) after the leading `Accts` object; parse only that part.
- Payset commitment and certificate are unverified (issue #23).
- Payset commitment: header `txn` = nothing before v11, flat `H("PF" + msgpack array of stored txns)` for v11 to v25, SHA-512/256
  Merkle tree from v26 (leaf `"TL" + txid + H("STIB" + stored txn entry)`, node `"MA"`, odd node paired with zeros, zero for an
  empty payset). `txn256`/`txn512` are vector commitments: padded to a power of two (zero or one element stay one leaf), bit-reversed
  leaf order, padding leaf `H("MB")`. Both use the SHA-256 txid and SHA-256 entry hash as leaf content, `txn512` just hashes with SHA-512.
- From v16 (`RequireGenesisHash`) blocks never flag `hgh`: the genesis hash is part of every transaction id regardless.
