# Ingestion

Sequential, replayable round processing per chain:

1. Mark round `processing` in the ingestion journal.
2. Fetch block via `IChainAdapter`, derive effects via `IEffectNormalizer`, compute ordered `balance_after`.
3. Write through `IRoundWriter` (idempotent on `EffectId`).
4. Publish via `IChainStatusStore.PublishRoundAsync` as the final step.

Implementation follows in a dedicated issue.
