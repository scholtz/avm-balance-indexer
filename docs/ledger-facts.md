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
- Certificate signatures are unverified (ADR 0004); the payset commitment is verified (see below).
- Payset commitment: header `txn` = nothing before v11, flat `H("PF" + msgpack array of stored txns)` for v11 to v25, SHA-512/256
  Merkle tree from v26 (leaf `"TL" + txid + H("STIB" + stored txn entry)`, node `"MA"`, odd node paired with zeros, zero for an
  empty payset). `txn256`/`txn512` are vector commitments: padded to a power of two (zero or one element stay one leaf), bit-reversed
  leaf order, padding leaf `H("MB")`. Both use the SHA-256 txid and SHA-256 entry hash as leaf content, `txn512` just hashes with SHA-512.
- From v16 (`RequireGenesisHash`) blocks never flag `hgh`: the genesis hash is part of every transaction id regardless.
- Voi archival gossip hosts are patchy (many lack blocks) but the fetcher fails over; as of 2026-10-11 `/v2/deltas` returns 404 for every Voi round, and the indexer refuses `round=`. A recent Voi window is verified by summing indexer transactions (inner ones flattened) per account against our transaction effects.
- ASA rules the normalizer relies on: opt-in is a zero `axfer` from an account to itself; `aclose` sends the sender's remaining units (apply data `aca`) and removes its holding; clawback (`asnd`) moves units from the named account while the sender only pays the fee; creating an asset gives the creator the whole `apar.t` and the id is `caid` of the apply data; destroying an asset (`acfg` with `caid`, no `apar`, sent by the manager) removes the creator's whole holding, which the block does not state, so the creator and total are kept from creation (token record, `MetadataJson.total`).
- `/v2/deltas` `AssetResources`: `Holding.Deleted` means the holding was removed (opt-out, destroy), `Holding.Holding` is `{}` for a zero holding and null when only the params changed. The public indexer reports `total: 0` for a destroyed asset, so its supply must be read from the creation transaction.
- ARC-200 transfer event: log of exactly 100 bytes = 4-byte selector (first 4 bytes of SHA-512/256 of `arc200_Transfer(address,address,uint256)`) + from (32) + to (32) + uint256 big-endian; zero address `from` is a mint, `to` a burn. The emitting application is the called one (`apid` of the transaction) or, for a creation, `apid` of the apply data. Logs are `dt.lg` of the transaction's apply data, inner calls carry their own. The Voi ARC indexer (`voi-mainnet-mimirapi.nftnavigator.xyz/arc200/transfers?round=N`) lists them per round (min/max round filters are ignored).
- Blocks of the early protocols (up to about 2021) have no `caid` in the apply data of an asset creation: the asset id is the block's transaction counter `tc` minus the number of transactions plus the transaction index plus one (verified: USDC 31566704 in round 8874561). Later blocks carry `caid`, also for inner creations.
- ARC-20 Smart ASAs (reference contract: name SMART-ASA, unit S-ASA) are created by their controlling application as an inner `acfg`, start `df` (frozen) and hold the application account as clawback `c`; the application account is SHA-512/256("appID" + big-endian id). The application id is only in off-chain metadata, but these three facts identify it on chain. A reconfiguration (`acfg` with `caid` and `apar`) can only change `m`, `r`, `f`, `c`; an omitted address is cleared.

