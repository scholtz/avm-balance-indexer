# ADR 0005: ASA effects and token records

- Status: accepted
- Date: 2026-10-11
- Issue: #18

## Context

ASA balances follow from `axfer` and `acfg` transactions, but some effects are not stated in a block: destroying an asset
removes the creator's holding, and closing out moves a remainder that is only in the apply data. Opt-in is an effect that
changes no balance.

## Decision

- The normalizer reads asset fields from the raw transaction map (`RawTransaction`) so top-level and inner transactions are
  handled the same way, independent of the SDK transaction subclass.
- Zero-delta effects are kept for opt-in, opt-out, creation and destroy (the storage derives `opted_in` from the latest effect:
  everything but `AssetOptOut`). A destroy writes `AssetDestroy` (minus the supply) followed by a zero `AssetOptOut`, a close-out
  writes `AssetOptOut` carrying the closed amount.
- Destroying needs state. `RoundIndexer` pre-scans a block for destroyed assets (`AssetScanner`), loads their token records from
  the token store and passes them as `IAssetDirectory`; assets created in the same block are tracked inside the normalizer.
  The creation writes a token record (creator, decimals, unit, name, `MetadataJson.total`, created round); a destroy writes it again
  with `DeletedRound`.
- Token records are written before the round is published. A crash in between leaves rows that replaying the round overwrites with
  the same values; writing them after publication could lose them for good.
- A redundant opt-in (zero self transfer of an account that is already opted in) is recorded as an `AssetOptIn` effect with no
  balance change; the normalizer is stateless and cannot tell.

## Consequences

- Indexing from genesis in order is required (already the case): an asset destroyed without a stored creation is an error.
- ARC-20 classification, asset reconfiguration (#34) and ARC-200 (#33) build on the same token records.
