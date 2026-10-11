# ADR 0010: Transactions from raw msgpack, SDK for the header only

- Status: accepted
- Date: 2026-10-11
- Issue: #45
- Amends: ADR 0002

## Context

ADR 0002 used the Algorand5 SDK to decode the certified block, transactions included, and a raw reader only for the apply data the
model drops. MainNet round 64000338 contains a payment authorised by a LogicSig with a 1805 byte program and a 1226 byte
argument that the SDK cannot decode (`MessageTypeException` at the LogicSig), so the indexer stalled there for good. The SDK
also decoded every transaction a second time.

## Decision

- `BlockDecoder` decodes only the header and certificate with the SDK: the payset (`txns`) is stripped from the payload first
  (`BlockPayload.WithoutTransactions`). `BlockData.Block.Transactions` is therefore always empty.
- Transactions come from the msgpack walk of `ApplyDataReader` (`TxnApplyData.RawTxn`, apply data, inner transactions) and are
  interpreted with `RawTransaction` (type, sender, fee, payment, asset and application fields). The normalizer uses nothing else.
- The id and payset hashes were already computed from the raw bytes.

## Consequences

- A transaction shape the SDK model cannot represent no longer matters; only the fields the indexer reads have to be right,
  and they are covered by ledger comparisons on every era.
- Do not reintroduce SDK transaction decoding on this path.
- The payload is walked several times (strip, apply data, block hash, payset commitment); merging the walks is a possible
  throughput optimisation (#15).
