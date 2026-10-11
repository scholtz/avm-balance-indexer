# ADR 0004: Block trust model

- Status: accepted
- Date: 2026-10-11
- Issue: #23

## Context

Blocks come from archival gossip hosts that may be hostile or buggy. Verifying the consensus certificate (vote
credentials, signatures and VRF proofs against the online stake of a past round) needs the participation tree of that
round, which a block-only indexer does not have.

## Decision

Every block is accepted only if:

1. its header hashes to the certificate's proposal digest and links to the previous block's hash (chain linking);
2. its genesis hash and id equal the pinned chain profile, and the served genesis file hashes to the pinned genesis hash;
3. its transactions reproduce the header payset commitments (`txn` per protocol: none before v11, flat v11 to v25, SHA-512/256
   Merkle from v26; `txn256` and `txn512` vector commitments when the header carries them), see `PaysetCommitment`.

Certificate signatures are **not** verified. The trust boundary is therefore: the header chain from the pinned genesis
is internally consistent and the transactions belong to it, but a host able to fabricate a whole alternative chain
(valid hashes, no consensus) is not detected. Mitigations: pinned genesis, running your own archival nodes or nodes you
trust, and the sampled comparison against ledger deltas in the live tests. Verifying certificates can be added later by
reading the stake from a trusted state proof source; it is out of scope here.

Before protocol v11 the header does not commit to the payset at all, so those transactions are bound only by the host.
Transaction ids of v16 and later blocks always include the restored genesis hash (`RequiresGenesisHash`); earlier
blocks restore it only when flagged.

## Consequences

- A host can no longer alter, add, drop or reorder transactions or their apply data of a block of v11 or later.
- Adding a protocol that changes the commitment scheme means updating `ConsensusProtocols`; unknown protocols are treated as
  the newest scheme and fail loudly (validation error) if that is wrong.
