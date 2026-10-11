# ADR 0009: ARC-20 classification from chain data

- Status: accepted
- Date: 2026-10-11
- Issue: #34

## Context

ARC-20 Smart ASAs keep an ordinary ASA balance. The controlling application id is part of off-chain asset metadata (ARC-3 / ARC-69),
which a block-only indexer cannot read.

## Decision

- An asset is an ARC-20 Smart ASA when it was created by an application as an inner transaction, starts frozen, and has that
  application's account as clawback. The application id is the issuing application (`ControllingAppId`), the classification is
  evidence-based (all three facts are on chain; another contract could create such an asset, so it is not a formal verification of the ARC-20 interface), and the record has `Standard = Arc20` with `StandardVerified = false` (detection is by chain evidence, not by the ARC-20 interface). Everything else is a plain ASA.
- The management addresses (and the whole record) follow `acfg` reconfigurations; the classification is re-evaluated, so an asset
  whose clawback moves away from the application stops being reported as Smart ASA. The record carries the addresses as text in
  its metadata json.
- Reconfiguring needs the stored record, like destroying: `RoundIndexer` loads the records of every asset a block destroys or
  reconfigures. A reconfiguration of an asset without a record is ignored (it moves no units).
- Created asset ids: blocks of the early protocols do not record them in the apply data, so the id is derived from the
  block's transaction counter there.

## Consequences

- Smart ASAs created by hand (outside the application) with the application's clawback are not recognised: only the creation by the
  application proves the link on chain.
- No off-chain request is made while indexing.
- Records written by the previous version carry no addresses or issuing application: a reconfiguration keeps their standard as
  stored, and a Smart ASA created before would stay a plain ASA, so a database indexed before this change should be rebuilt.
