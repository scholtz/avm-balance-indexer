# ADR 0008: ARC-200 balances from logs

- Status: accepted
- Date: 2026-10-11
- Issue: #33

## Context

ARC-200 balances live in application state in a layout every contract chooses. The standard requires a transfer event for every
movement, so the logs are the one common source. They are also written by the application itself, so they can be incomplete
(a contract that assigns its supply at creation without a mint event) or forged.

## Decision

- Index `arc200_Transfer` logs (top-level and inner application calls) as effects of `kind=arc200, token_id=application id`.
  Detection is by event shape only, so the token record has `Standard = Arc200` and `StandardVerified = false`.
- Balances are therefore lower bounds when a contract moves tokens without logging. A debit larger than the known balance is
  limited to it by the balance tracker, together with the credit half of the same transfer, instead of failing the round:
  an application must never be able to stop the indexer. Native and ASA deltas still fail loudly on underflow.
- Amounts are uint256, so PostgreSQL amounts are widened to `numeric(78,0)` (`0003_wide_amounts.sql`) and read as `BigInteger`
  (a `decimal` overflows near 7.9e28). Cassandra stores decimal text and needed no change.
- A balance is also capped at 2^256 - 1 (what the column holds), so a forged mint cannot overflow the database either.
- The ARC-200 token record is written the first time a token is seen (cached per process, checked against the store once).
  Its created round is the round of the first transfer seen, not of the application's creation.

## Consequences

- Any application that logs 100-byte transfer-shaped events gets a token record and holdings (unverified, `StandardVerified = false`);
  filtering by verification is left to readers until #34-style classification exists. A limited debit is stored as an ordinary
  transfer with the smaller delta, without a marker.
- `opted_in` is not meaningful for ARC-200 (it is true for any holding).

- Mimir (the Voi ARC indexer) is the live oracle for the transfers; balances cannot be checked mid-chain without contract state.
- Metadata (name, symbol, decimals) of ARC-200 tokens needs application calls and is not part of this decision.
