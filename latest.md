# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-11 at commit `e3803b2e`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,838,766 to 66,848,765) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 176 rounds/s | 332 rounds/s |
| Effects written | 415,637 (7,309/s) | 415,637 (13,792/s) |
| Time for the range | 56.9 s | 30.1 s |
| Stored size | 75.6 MiB | 289.5 MiB |
| Per round | 7.74 KiB | 29.65 KiB |
| Per effect | 183 bytes | 701 bytes |
| If all 66 M MainNet rounds were like these | 0.5 TiB | 1.8 TiB |

Block download is the same for both and not part of the table: 240 rounds/s from the public archival nodes (116,496 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 37.7 MiB, effects_by_token_bucket 28.5 MiB, effect_buckets_by_account_token 3.6 MiB, holdings_by_token 1.8 MiB, accounts_touched_by_token 1.7 MiB
- PostgreSQL 17.11: effects_by_account_token 152.2 MiB, effects_by_token_bucket 112.7 MiB, holdings_by_token 7.8 MiB, accounts_touched_by_token 7.8 MiB, holdings_by_account 7.0 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 3 ARC-200 tokens, 5,418 ASAs. 1,834 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 291,862 | 3,583 | 13,928 |
| asa:849191641 |  | 49,041 | 2,826 | 8,931 |
| asa:31566704 |  | 39,455 | 330 | 3,372 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,522 | 12 | 21 |
| asa:2543151056 |  | 1,578 | 402 | 403 |
| asa:2726252423 |  | 1,054 | 30 | 51 |
| asa:452399768 |  | 1,016 | 58 | 75 |
| asa:3203964481 |  | 608 | 36 | 52 |
| asa:2994233666 |  | 582 | 27 | 66 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.8 / 1.4 / 3.0 ms; 4,311 req/s, p95 7 ms | 0.6 / 0.9 / 1.5 ms; 4,718 req/s, p95 5 ms |
| account holdings | 1.5 / 2.7 / 3.7 ms; 2,113 req/s, p95 13 ms | 1.4 / 2.0 / 3.4 ms; 2,258 req/s, p95 11 ms |
| balance, latest | 2.6 / 5.3 / 7.4 ms; 1,681 req/s, p95 18 ms | 2.2 / 2.9 / 3.5 ms; 1,529 req/s, p95 16 ms |
| balance, past round | 2.0 / 2.5 / 3.4 ms; 2,058 req/s, p95 12 ms | 1.8 / 2.5 / 3.1 ms; 1,694 req/s, p95 14 ms |
| history, busiest account | 5.1 / 9.0 / 14.1 ms; 840 req/s, p95 36 ms | 4.7 / 6.0 / 7.3 ms; 916 req/s, p95 26 ms |
| history, typical account | 1.2 / 1.6 / 2.5 ms; 4,137 req/s, p95 6 ms | 1.5 / 2.5 / 5.1 ms; 2,123 req/s, p95 12 ms |
| holders, native, latest | 13.3 / 15.3 / 18.3 ms; 320 req/s, p95 66 ms | 2.3 / 3.3 / 3.6 ms; 1,202 req/s, p95 20 ms |
| holders, native, past round | 20.2 / 23.7 / 28.4 ms; 100 req/s, p95 194 ms | 7.1 / 7.7 / 9.5 ms; 305 req/s, p95 77 ms |
| holders, busiest ASA, latest | 9.9 / 11.1 / 13.5 ms; 390 req/s, p95 55 ms | 2.0 / 2.5 / 3.4 ms; 1,200 req/s, p95 19 ms |
| holders, busiest ARC-200, latest | 1.4 / 2.0 / 2.8 ms; 2,492 req/s, p95 10 ms | 1.2 / 1.3 / 1.6 ms; 2,815 req/s, p95 8 ms |
| token info | 0.4 / 0.5 / 0.6 ms; 10,368 req/s, p95 3 ms | 0.5 / 0.5 / 0.6 ms; 6,802 req/s, p95 4 ms |

## voi

Measured on 2026-11-11 at commit `e3803b2e`: the 100,000 newest rounds of voi (chain rounds 24,329,970 to 24,429,969) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 5,019 rounds/s | 8,477 rounds/s |
| Effects written | 32,893 (1,651/s) | 32,893 (2,788/s) |
| Time for the range | 19.9 s | 11.8 s |
| Stored size | 2.8 MiB | 21.7 MiB |
| Per round | 0.03 KiB | 0.22 KiB |
| Per effect | 89 bytes | 690 bytes |

Block download is the same for both and not part of the table: 61 rounds/s from the public archival nodes (6,780 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.6 MiB, effects_by_token_bucket 1.1 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 13.1 MiB, effects_by_token_bucket 8.0 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 24 ARC-200 tokens, 5 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 29,275 | 62 | 104 |
| arc200:390001 |  | 920 | 11 | 21 |
| asa:586896 |  | 490 | 1 | 2 |
| arc200:395614 |  | 408 | 5 | 14 |
| arc200:420069 |  | 362 | 17 | 26 |
| arc200:300279 |  | 232 | 19 | 27 |
| arc200:47138068 |  | 212 | 7 | 11 |
| arc200:412682 |  | 174 | 6 | 13 |
| asa:302190 |  | 134 | 6 | 11 |
| arc200:404004 |  | 94 | 16 | 19 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.7 / 0.9 / 1.4 ms; 4,284 req/s, p95 7 ms | 0.6 / 0.7 / 1.1 ms; 4,514 req/s, p95 6 ms |
| account holdings | 1.2 / 1.7 / 7.0 ms; 2,494 req/s, p95 11 ms | 1.4 / 1.9 / 2.8 ms; 2,129 req/s, p95 12 ms |
| balance, latest | 2.0 / 2.6 / 2.9 ms; 2,072 req/s, p95 13 ms | 2.1 / 2.7 / 3.3 ms; 1,693 req/s, p95 14 ms |
| balance, past round | 2.0 / 2.9 / 5.4 ms; 2,209 req/s, p95 11 ms | 1.6 / 2.1 / 2.7 ms; 1,942 req/s, p95 12 ms |
| history, busiest account | 5.3 / 8.2 / 11.3 ms; 650 req/s, p95 48 ms | 3.3 / 4.5 / 5.1 ms; 924 req/s, p95 25 ms |
| history, typical account | 1.4 / 2.6 / 3.4 ms; 2,481 req/s, p95 11 ms | 1.5 / 2.8 / 4.0 ms; 1,784 req/s, p95 14 ms |
| holders, native, latest | 2.4 / 3.9 / 6.8 ms; 938 req/s, p95 30 ms | 2.1 / 3.2 / 4.0 ms; 1,486 req/s, p95 15 ms |
| holders, native, past round | 7.6 / 10.7 / 14.1 ms; 210 req/s, p95 99 ms | 6.8 / 7.5 / 7.9 ms; 334 req/s, p95 71 ms |
| holders, busiest ASA, latest | 1.3 / 1.7 / 2.0 ms; 2,434 req/s, p95 11 ms | 1.2 / 1.3 / 1.8 ms; 3,044 req/s, p95 7 ms |
| holders, busiest ARC-200, latest | 1.6 / 1.9 / 2.6 ms; 1,644 req/s, p95 17 ms | 1.3 / 1.5 / 1.9 ms; 2,496 req/s, p95 9 ms |
| token info | 0.4 / 0.5 / 0.9 ms; 9,326 req/s, p95 3 ms | 0.4 / 0.5 / 0.6 ms; 8,246 req/s, p95 3 ms |

