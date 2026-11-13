# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-13 at commit `e3803b2e`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,901,791 to 66,911,790) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 77 rounds/s | 368 rounds/s |
| Effects written | 958,453 (7,353/s) | 958,453 (35,309/s) |
| Time for the range | 130.4 s | 27.1 s |
| Stored size | 153.1 MiB | 628.2 MiB |
| Per round | 15.68 KiB | 64.32 KiB |
| Per effect | 165 bytes | 678 bytes |
| If all 66 M MainNet rounds were like these | 1.0 TiB | 4.0 TiB |

Block download is the same for both and not part of the table: 320 rounds/s from the public archival nodes (277,318 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 79.0 MiB, effects_by_token_bucket 62.1 MiB, effect_buckets_by_account_token 4.5 MiB, holdings_by_account 2.3 MiB, holdings_by_token 2.2 MiB
- PostgreSQL 17.12: effects_by_account_token 342.1 MiB, effects_by_token_bucket 256.4 MiB, accounts_touched_by_token 9.2 MiB, holdings_by_token 9.2 MiB, holdings_by_account 8.5 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 6,864 ASAs. 3,131 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 618,728 | 5,664 | 12,890 |
| asa:3145862805 |  | 93,719 | 9,420 | 9,421 |
| asa:31566704 |  | 60,894 | 452 | 4,096 |
| asa:127746157 |  | 59,306 | 0 | 1 |
| asa:127745593 |  | 27,741 | 0 | 1 |
| asa:849191641 |  | 21,066 | 1,178 | 4,887 |
| asa:127746786 |  | 16,153 | 0 | 1 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,278 | 17 | 29 |
| asa:3203964481 |  | 3,286 | 62 | 80 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.3 / 2.3 ms; 2,525 req/s, p95 12 ms | 0.6 / 0.7 / 0.8 ms; 5,278 req/s, p95 5 ms |
| account holdings | 1.9 / 3.8 / 5.0 ms; 1,653 req/s, p95 19 ms | 1.3 / 2.0 / 2.7 ms; 2,485 req/s, p95 10 ms |
| balance, latest | 3.0 / 5.5 / 6.6 ms; 1,172 req/s, p95 25 ms | 1.8 / 2.2 / 2.7 ms; 1,975 req/s, p95 12 ms |
| balance, past round | 2.4 / 2.8 / 4.1 ms; 1,752 req/s, p95 14 ms | 1.6 / 2.0 / 3.0 ms; 2,139 req/s, p95 11 ms |
| history, busiest account | 6.4 / 12.0 / 15.5 ms; 609 req/s, p95 51 ms | 3.9 / 5.8 / 6.7 ms; 690 req/s, p95 39 ms |
| history, typical account | 1.7 / 1.9 / 2.3 ms; 2,585 req/s, p95 11 ms | 1.2 / 1.5 / 1.9 ms; 2,884 req/s, p95 9 ms |
| holders, native, latest | 10.7 / 12.8 / 16.8 ms; 330 req/s, p95 62 ms | 1.8 / 2.5 / 3.3 ms; 1,431 req/s, p95 17 ms |
| holders, native, past round | 36.4 / 55.7 / 67.1 ms; 61 req/s, p95 310 ms | 5.8 / 6.5 / 6.8 ms; 447 req/s, p95 54 ms |
| holders, busiest ASA, latest | 3.8 / 5.1 / 6.7 ms; 587 req/s, p95 46 ms | 1.7 / 2.1 / 2.1 ms; 1,520 req/s, p95 16 ms |
| token info | 0.7 / 0.8 / 0.8 ms; 6,731 req/s, p95 4 ms | 0.4 / 0.5 / 0.7 ms; 8,416 req/s, p95 3 ms |

## voi

Measured on 2026-11-13 at commit `e3803b2e`: the 100,000 newest rounds of voi (chain rounds 24,392,578 to 24,492,577) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 2,843 rounds/s | 11,720 rounds/s |
| Effects written | 38,709 (1,100/s) | 38,709 (4,537/s) |
| Time for the range | 35.2 s | 8.5 s |
| Stored size | 3.0 MiB | 24.7 MiB |
| Per round | 0.03 KiB | 0.25 KiB |
| Per effect | 81 bytes | 666 bytes |

Block download is the same for both and not part of the table: 58 rounds/s from the public archival nodes (6,923 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.7 MiB, effects_by_token_bucket 1.2 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, holdings_by_account 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 14.7 MiB, effects_by_token_bucket 9.3 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 23 ARC-200 tokens, 10 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 36,718 | 133 | 167 |
| arc200:420069 |  | 378 | 18 | 25 |
| arc200:390001 |  | 324 | 10 | 21 |
| asa:586896 |  | 322 | 1 | 2 |
| arc200:300279 |  | 158 | 18 | 26 |
| arc200:412682 |  | 118 | 7 | 14 |
| arc200:47138068 |  | 104 | 6 | 10 |
| arc200:395614 |  | 84 | 4 | 10 |
| arc200:404004 |  | 80 | 16 | 19 |
| arc200:411311 |  | 68 | 14 | 15 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 1.2 / 1.5 ms; 2,752 req/s, p95 11 ms | 0.6 / 0.8 / 1.4 ms; 5,573 req/s, p95 4 ms |
| account holdings | 1.8 / 2.1 / 3.1 ms; 1,713 req/s, p95 15 ms | 1.3 / 1.6 / 2.3 ms; 2,503 req/s, p95 10 ms |
| balance, latest | 2.7 / 3.2 / 4.1 ms; 1,526 req/s, p95 16 ms | 1.7 / 1.9 / 2.2 ms; 2,057 req/s, p95 12 ms |
| balance, past round | 2.6 / 3.1 / 3.7 ms; 1,736 req/s, p95 14 ms | 1.5 / 1.8 / 2.2 ms; 2,176 req/s, p95 10 ms |
| history, busiest account | 6.6 / 9.4 / 13.4 ms; 709 req/s, p95 40 ms | 4.1 / 5.1 / 6.0 ms; 740 req/s, p95 36 ms |
| history, typical account | 1.7 / 2.9 / 3.9 ms; 2,311 req/s, p95 12 ms | 1.0 / 1.7 / 2.1 ms; 3,079 req/s, p95 8 ms |
| holders, native, latest | 5.4 / 6.9 / 8.9 ms; 479 req/s, p95 51 ms | 1.7 / 2.2 / 2.7 ms; 1,617 req/s, p95 14 ms |
| holders, native, past round | 18.6 / 22.5 / 27.4 ms; 92 req/s, p95 208 ms | 5.1 / 5.5 / 6.1 ms; 506 req/s, p95 47 ms |
| holders, busiest ASA, latest | 2.0 / 2.5 / 3.0 ms; 1,812 req/s, p95 13 ms | 0.9 / 1.0 / 1.2 ms; 4,265 req/s, p95 6 ms |
| holders, busiest ARC-200, latest | 2.5 / 3.3 / 3.9 ms; 1,185 req/s, p95 22 ms | 1.1 / 1.3 / 1.5 ms; 3,000 req/s, p95 7 ms |
| token info | 0.7 / 0.8 / 0.8 ms; 6,662 req/s, p95 4 ms | 0.3 / 0.4 / 0.4 ms; 9,999 req/s, p95 3 ms |

