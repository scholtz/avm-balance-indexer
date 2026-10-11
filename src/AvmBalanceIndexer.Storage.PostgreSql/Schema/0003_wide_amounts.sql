-- ARC-200 balances are uint256 (up to 78 decimal digits); the first schema sized amounts for uint64.
-- Widening a numeric column is a metadata-only change in PostgreSQL, and a no-op once applied.

ALTER TABLE holdings_by_account ALTER COLUMN amount TYPE numeric(78,0);
ALTER TABLE holdings_by_token ALTER COLUMN amount TYPE numeric(78,0);
ALTER TABLE effects_by_account_token ALTER COLUMN delta TYPE numeric(78,0), ALTER COLUMN balance_after TYPE numeric(78,0);
ALTER TABLE effects_by_token_bucket ALTER COLUMN delta TYPE numeric(78,0);
