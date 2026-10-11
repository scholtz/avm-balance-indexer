-- ARC-200 balances are uint256 (up to 78 decimal digits); the first schema sized amounts for uint64.
-- Applied once: the statements take exclusive locks, so they are skipped when the columns are already wide.

DO $$
BEGIN
  IF (SELECT count(*) FROM information_schema.columns
      WHERE table_schema = current_schema() AND numeric_precision = 78
        AND ((table_name IN ('holdings_by_account', 'holdings_by_token') AND column_name = 'amount')
          OR (table_name = 'effects_by_account_token' AND column_name IN ('delta', 'balance_after'))
          OR (table_name = 'effects_by_token_bucket' AND column_name = 'delta'))) <> 5 THEN
    ALTER TABLE holdings_by_account ALTER COLUMN amount TYPE numeric(78,0);
    ALTER TABLE holdings_by_token ALTER COLUMN amount TYPE numeric(78,0);
    ALTER TABLE effects_by_account_token ALTER COLUMN delta TYPE numeric(78,0), ALTER COLUMN balance_after TYPE numeric(78,0);
    ALTER TABLE effects_by_token_bucket ALTER COLUMN delta TYPE numeric(78,0);
  END IF;
END $$;
