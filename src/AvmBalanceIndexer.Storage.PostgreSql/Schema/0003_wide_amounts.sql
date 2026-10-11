-- ARC-200 balances are uint256 (up to 78 decimal digits); the first schema sized amounts for uint64.
-- Applied once: the statements take exclusive locks, so they are skipped when the columns are already wide.

DO $$
BEGIN
  IF (SELECT numeric_precision FROM information_schema.columns
      WHERE table_schema = current_schema() AND table_name = 'holdings_by_account' AND column_name = 'amount') IS DISTINCT FROM 78 THEN
    ALTER TABLE holdings_by_account ALTER COLUMN amount TYPE numeric(78,0);
    ALTER TABLE holdings_by_token ALTER COLUMN amount TYPE numeric(78,0);
    ALTER TABLE effects_by_account_token ALTER COLUMN delta TYPE numeric(78,0), ALTER COLUMN balance_after TYPE numeric(78,0);
    ALTER TABLE effects_by_token_bucket ALTER COLUMN delta TYPE numeric(78,0);
  END IF;
END $$;
