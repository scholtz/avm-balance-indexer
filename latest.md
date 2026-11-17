# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-17 at commit `5143607e`: the 10,000 newest rounds of algorand-mainnet (chain rounds 67,028,341 to 67,038,340) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 103 rounds/s | 250 rounds/s |
| Effects written | 950,640 (9,806/s) | 950,640 (23,750/s) |
| Time for the range | 96.9 s | 40.0 s |
| Stored size | 158.0 MiB | 629.9 MiB |
| Per round | 16.18 KiB | 64.50 KiB |
| Per effect | 171 bytes | 682 bytes |
| If all 66 M MainNet rounds were like these | 1.0 TiB | 4.0 TiB |

Block download is the same for both and not part of the table: 329 rounds/s from the public archival nodes (274,678 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 79.8 MiB, effects_by_token_bucket 63.5 MiB, effect_buckets_by_account_token 5.5 MiB, holdings_by_account 2.8 MiB, holdings_by_token 2.7 MiB
- PostgreSQL 17.12: effects_by_account_token 338.8 MiB, effects_by_token_bucket 255.0 MiB, holdings_by_token 11.3 MiB, accounts_touched_by_token 11.2 MiB, holdings_by_account 10.3 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 9 ARC-200 tokens, 7,579 ASAs. 4,343 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 620,140 | 7,044 | 17,278 |
| asa:3145862805 |  | 93,032 | 8,359 | 8,360 |
| asa:849191641 |  | 61,403 | 3,836 | 10,840 |
| asa:127746157 |  | 54,267 | 0 | 1 |
| asa:127745593 |  | 30,628 | 0 | 1 |
| asa:31566704 |  | 22,339 | 380 | 4,366 |
| asa:127746786 |  | 18,360 | 0 | 1 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,257 | 17 | 29 |
| asa:2726252423 |  | 2,762 | 94 | 126 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.4 / 3.0 / 4.9 ms; 3,597 req/s, p95 9 ms | 0.6 / 0.7 / 0.8 ms; 5,397 req/s, p95 4 ms |
| account holdings | 2.1 / 4.1 / 5.8 ms; 2,049 req/s, p95 14 ms | 1.4 / 1.8 / 2.9 ms; 2,394 req/s, p95 9 ms |
| balance, latest | 3.0 / 5.0 / 7.3 ms; 1,731 req/s, p95 15 ms | 2.0 / 2.7 / 3.8 ms; 1,818 req/s, p95 13 ms |
| balance, past round | 2.6 / 3.0 / 4.5 ms; 1,836 req/s, p95 13 ms | 1.6 / 2.0 / 2.5 ms; 2,171 req/s, p95 11 ms |
| history, busiest account | 5.7 / 11.0 / 14.8 ms; 766 req/s, p95 38 ms | 2.5 / 3.6 / 4.8 ms; 1,065 req/s, p95 22 ms |
| history, typical account | 1.5 / 1.7 / 2.4 ms; 3,880 req/s, p95 8 ms | 1.4 / 1.6 / 3.1 ms; 2,802 req/s, p95 9 ms |
| holders, native, latest | 8.7 / 10.9 / 13.1 ms; 380 req/s, p95 61 ms | 2.0 / 2.7 / 3.3 ms; 1,414 req/s, p95 17 ms |
| holders, native, past round | 26.9 / 32.9 / 37.3 ms; 75 req/s, p95 264 ms | 5.9 / 6.5 / 6.9 ms; 389 req/s, p95 63 ms |
| holders, busiest ASA, latest | 3.2 / 4.2 / 6.4 ms; 612 req/s, p95 47 ms | 1.8 / 2.7 / 3.3 ms; 1,478 req/s, p95 16 ms |
| holders, busiest ARC-200, latest | 1.7 / 2.0 / 3.0 ms; 2,266 req/s, p95 11 ms | 1.2 / 1.5 / 2.1 ms; 3,222 req/s, p95 7 ms |
| token info | 0.6 / 0.7 / 0.7 ms; 10,935 req/s, p95 3 ms | 0.4 / 0.5 / 0.5 ms; 7,036 req/s, p95 4 ms |

## voi

Measured on 2026-11-17 at commit `5143607e`: the 100,000 newest rounds of voi (chain rounds 24,518,142 to 24,618,141) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 5,605 rounds/s | 10,178 rounds/s |
| Effects written | 23,194 (1,300/s) | 23,194 (2,361/s) |
| Time for the range | 17.8 s | 9.8 s |
| Stored size | 1.9 MiB | 15.9 MiB |
| Per round | 0.02 KiB | 0.16 KiB |
| Per effect | 85 bytes | 718 bytes |

Block download is the same for both and not part of the table: 61 rounds/s from the public archival nodes (5,432 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.1 MiB, effects_by_token_bucket 0.7 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_account 0.0 MiB, holdings_by_token 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 9.7 MiB, effects_by_token_bucket 5.7 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 13 ARC-200 tokens, 3 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 22,355 | 59 | 86 |
| asa:586896 |  | 344 | 1 | 2 |
| arc200:300279 |  | 66 | 14 | 15 |
| arc200:404004 |  | 66 | 14 | 15 |
| arc200:411311 |  | 66 | 14 | 15 |
| arc200:416000 |  | 66 | 14 | 15 |
| arc200:420019 |  | 60 | 13 | 14 |
| arc200:390001 |  | 56 | 3 | 10 |
| arc200:395614 |  | 36 | 2 | 5 |
| asa:302190 |  | 24 | 1 | 2 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.4 / 2.2 ms; 3,691 req/s, p95 8 ms | 0.8 / 0.9 / 0.9 ms; 4,522 req/s, p95 6 ms |
| account holdings | 1.7 / 2.2 / 7.5 ms; 2,184 req/s, p95 14 ms | 1.6 / 1.9 / 2.8 ms; 2,304 req/s, p95 11 ms |
| balance, latest | 2.6 / 3.3 / 3.8 ms; 1,883 req/s, p95 13 ms | 2.3 / 3.5 / 5.0 ms; 1,634 req/s, p95 14 ms |
| balance, past round | 2.6 / 3.1 / 3.6 ms; 1,963 req/s, p95 12 ms | 1.9 / 2.3 / 2.7 ms; 1,776 req/s, p95 13 ms |
| history, busiest account | 6.7 / 8.4 / 9.4 ms; 757 req/s, p95 36 ms | 4.9 / 6.2 / 7.2 ms; 613 req/s, p95 43 ms |
| history, typical account | 1.6 / 2.9 / 3.5 ms; 2,404 req/s, p95 12 ms | 1.5 / 3.1 / 3.9 ms; 2,048 req/s, p95 13 ms |
| holders, native, latest | 2.6 / 3.5 / 4.8 ms; 1,018 req/s, p95 26 ms | 1.9 / 2.4 / 3.7 ms; 1,706 req/s, p95 14 ms |
| holders, native, past round | 7.3 / 10.7 / 17.2 ms; 234 req/s, p95 89 ms | 6.0 / 7.0 / 9.5 ms; 394 req/s, p95 64 ms |
| holders, busiest ASA, latest | 1.6 / 1.9 / 2.4 ms; 2,506 req/s, p95 10 ms | 1.3 / 1.4 / 1.4 ms; 3,276 req/s, p95 7 ms |
| holders, busiest ARC-200, latest | 1.9 / 2.4 / 3.4 ms; 1,775 req/s, p95 14 ms | 1.4 / 1.6 / 2.1 ms; 2,687 req/s, p95 8 ms |
| token info | 0.6 / 0.7 / 0.7 ms; 9,707 req/s, p95 3 ms | 0.4 / 0.5 / 0.5 ms; 7,586 req/s, p95 4 ms |

