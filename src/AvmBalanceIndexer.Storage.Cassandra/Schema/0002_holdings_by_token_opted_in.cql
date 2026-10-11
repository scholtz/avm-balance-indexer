-- holdings_by_token carries the opt-in flag, so latest and historical holders agree. Rows written before the column existed read
-- as null and are resolved from the effect history.
-- Column additions are listed here, one per statement; the schema manager skips them when the column already exists.

ALTER TABLE holdings_by_token ADD IF NOT EXISTS opted_in boolean;
