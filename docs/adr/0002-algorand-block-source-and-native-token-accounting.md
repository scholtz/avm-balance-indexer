# ADR 0002: Archival gossip block source and exact native token accounting

- Status: accepted
- Date: 2026-10-11
- Issue: #13

## Context

The indexer must derive exact end-of-round ALGO/VOI balances for every account from archived blocks, using the
Algorand5 SDK to talk to the network. Three facts shaped the design.

1. **The SDK block fetcher is not enough on its own.** `GossipHttpClient.FetchBlockAsync` ignores HTTP status codes,
   never fails over to another host, and returns a model that drops the ledger apply data of every transaction. Without
   `rs`, `rr` and `rc` (rewards realized by the transaction) and `ca` (closing amount) balances cannot be exact. A
   TestNet transaction in round 190 carries a sender reward of 799,999,596 microAlgos.
2. **Some balance changes are not caused by any transaction.** The ledger withdraws the rewards pool balance every round,
   and TestNet had two manual hotfix transfers. A transaction-only indexer reports a wrong rewards pool balance.
3. **A block is not self-describing for transaction ids.** Blocks strip the genesis hash and id from stored transactions.

## Decision

- Use the SDK for what it does well: DNS SRV discovery of archival nodes, the `CertifiedBlock` codec and transaction
  models, the algod client for the latest round, and address handling. `GossipBlockFetcher` speaks the same gossip HTTP
  protocol but adds status handling, host failover with cooldowns, per-attempt timeouts, bounded reads, jittered
  exponential backoff and validation of every response (round, genesis id and hash, certificate, hash lengths).
- Read the raw payload a second time with a forward-only msgpack reader (`ApplyDataReader`) for the apply data the SDK
  discards. Both decodes are checked for identical transaction counts including inner transactions.
- Chain continuity: each block's previous hash must equal the published block hash, taken from the certificate
  proposal digest. A mismatch is a `ChainDivergenceException` and stops the indexer.
- Compute transaction ids from the canonical stored msgpack with the stripped genesis fields re-inserted in sorted key
  position. This is exact for every transaction type and independent of SDK model coverage. Verified against the
  public indexer for TestNet round 1.
- Effect order within a round is the ledger execution order: rewards pool withdrawal and manual adjustments, then per
  transaction rewards, fee, payment, close-out and inner transactions depth first, then the proposer payout. Intra
  round order is 0 for the start of the block, the transaction index plus one for transactions, and the transaction
  count plus one for the end of the block.
- The rewards pool withdrawal is derived from block headers only. The ledger debits the pool by
  `rewardUnits x (level - previous level)`, and since `level` and `residue` advance by `(rate + residue) / units` and
  `(rate + residue) % units`, the debit equals `rate + previous residue - residue`. Protocols before v31 use the previous
  rate in the round the rate is recalculated. Verified against ledger state deltas for the first 1000 rounds of TestNet
  and MainNet.
- Effect keys end in `(round, intra_round_order, ordinal)` where `ordinal` is the effect sequence number within the
  round. The reference design keyed on the transaction id, which collides when one transaction changes the same account
  twice (fee and payment). The secondary descending index on the account effect table is dropped; the primary key
  b-tree is read backwards for "latest effect at or before round R", avoiding double write cost on the largest table.

## Verification

`tests/AvmBalanceIndexer.Live.Tests` indexes the first 1000 rounds of Algorand TestNet and MainNet into the in-memory
reference and into PostgreSQL. For every round it compares the balance of every account the ledger changed with the
ledger state delta published by a public algod (`/v2/deltas/{round}`), checks that no indexed change lacks a ledger
change, compares transaction ids and amounts with a public indexer, and verifies that resuming mid-chain gives
identical balances.

## Consequences

- Block integrity relies on the certificate digest and hash chaining, not on verifying certificate signatures or the
  block hash itself. Gossip hosts are trusted as a data source. Operators should point `Algorand:GossipHosts` at their
  own archival nodes in production.
- Each transaction adds a fee credit effect for the fee sink, and every round adds a rewards pool effect while rewards
  are active. Those two accounts are single hot partitions; bucket sizes and sharding need load testing before MainNet.
- ASA, ARC-20 and ARC-200 effects, proposer payout verification against live payout-era blocks, and any other
  protocol-level implicit balance movement are not covered yet.
