# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-01 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,524,934 to 66,534,933) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 128 rounds/s | 321 rounds/s |
| Effects written | 341,798 (4,372/s) | 341,798 (10,984/s) |
| Time for the range | 78.2 s | 31.1 s |
| Stored size | 63.7 MiB | 242.2 MiB |
| Per round | 6.52 KiB | 24.80 KiB |
| Per effect | 188 bytes | 713 bytes |
| If all 66 M MainNet rounds were like these | 0.4 TiB | 1.5 TiB |

Block download is the same for both and not part of the table: 261 rounds/s from the public archival nodes (91,408 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 31.0 MiB, effects_by_token_bucket 23.2 MiB, effect_buckets_by_account_token 3.6 MiB, holdings_by_token 1.8 MiB, accounts_touched_by_token 1.8 MiB
- PostgreSQL 17.11: effects_by_account_token 124.8 MiB, effects_by_token_bucket 92.3 MiB, holdings_by_token 7.8 MiB, accounts_touched_by_token 7.8 MiB, holdings_by_account 7.0 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 1 ARC-200 tokens, 6,468 ASAs. 2,736 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 254,426 | 4,548 | 13,367 |
| asa:849191641 |  | 38,830 | 2,970 | 8,393 |
| asa:31566704 |  | 18,156 | 379 | 4,323 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,746 | 15 | 17 |
| asa:452399768 |  | 896 | 53 | 64 |
| asa:2726252423 |  | 658 | 24 | 46 |
| asa:2200000000 |  | 642 | 60 | 116 |
| asa:1294765516 |  | 287 | 3 | 4 |
| asa:3178895177 |  | 252 | 39 | 57 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.2 / 3.0 / 4.8 ms; 3,133 req/s, p95 9 ms | 0.8 / 1.0 / 1.6 ms; 3,874 req/s, p95 7 ms |
| account holdings | 2.3 / 4.3 / 5.8 ms; 1,451 req/s, p95 21 ms | 1.8 / 2.4 / 3.6 ms; 1,970 req/s, p95 13 ms |
| balance, latest | 3.3 / 5.1 / 7.2 ms; 1,345 req/s, p95 20 ms | 2.5 / 3.3 / 3.7 ms; 1,373 req/s, p95 17 ms |
| balance, past round | 2.7 / 3.4 / 4.1 ms; 1,794 req/s, p95 14 ms | 2.2 / 2.9 / 3.4 ms; 1,661 req/s, p95 15 ms |
| history, busiest account | 6.5 / 11.0 / 13.2 ms; 713 req/s, p95 42 ms | 5.1 / 7.2 / 8.1 ms; 676 req/s, p95 44 ms |
| history, typical account | 1.8 / 2.8 / 3.7 ms; 1,718 req/s, p95 18 ms | 1.7 / 2.1 / 2.7 ms; 2,093 req/s, p95 13 ms |
| holders, native, latest | 12.6 / 13.8 / 14.5 ms; 306 req/s, p95 65 ms | 2.6 / 3.4 / 4.4 ms; 1,068 req/s, p95 21 ms |
| holders, native, past round | 30.4 / 38.5 / 42.5 ms; 64 req/s, p95 304 ms | 7.8 / 8.6 / 9.9 ms; 301 req/s, p95 81 ms |
| holders, busiest ASA, latest | 12.2 / 13.5 / 14.3 ms; 309 req/s, p95 64 ms | 2.5 / 3.1 / 3.3 ms; 1,110 req/s, p95 20 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.7 / 3.3 ms; 1,687 req/s, p95 15 ms | 1.4 / 1.7 / 2.3 ms; 2,880 req/s, p95 8 ms |
| token info | 0.7 / 0.9 / 1.3 ms; 5,480 req/s, p95 5 ms | 0.6 / 0.7 / 0.8 ms; 7,557 req/s, p95 4 ms |

## voi

Measured on 2026-11-01 at commit `9e2e5bda`: the 100,000 newest rounds of voi (chain rounds 24,016,980 to 24,116,979) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 3,439 rounds/s | 7,728 rounds/s |
| Effects written | 31,184 (1,072/s) | 31,184 (2,410/s) |
| Time for the range | 29.1 s | 12.9 s |
| Stored size | 2.5 MiB | 20.1 MiB |
| Per round | 0.03 KiB | 0.21 KiB |
| Per effect | 85 bytes | 673 bytes |

Block download is the same for both and not part of the table: 28 rounds/s from the public archival nodes (6,267 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.4 MiB, effects_by_token_bucket 1.0 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 12.0 MiB, effects_by_token_bucket 7.6 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 21 ARC-200 tokens, 4 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 28,518 | 48 | 77 |
| asa:586896 |  | 1,282 | 1 | 2 |
| arc200:390001 |  | 424 | 9 | 12 |
| arc200:395614 |  | 236 | 3 | 11 |
| arc200:47138068 |  | 114 | 4 | 10 |
| arc200:300279 |  | 106 | 14 | 18 |
| arc200:420069 |  | 90 | 7 | 14 |
| arc200:404004 |  | 66 | 13 | 14 |
| arc200:411311 |  | 66 | 13 | 14 |
| arc200:416000 |  | 66 | 13 | 14 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.3 / 1.6 ms; 2,675 req/s, p95 13 ms | 0.8 / 1.0 / 1.8 ms; 4,159 req/s, p95 6 ms |
| account holdings | 1.8 / 2.2 / 2.9 ms; 1,900 req/s, p95 15 ms | 1.7 / 2.0 / 3.0 ms; 2,008 req/s, p95 13 ms |
| balance, latest | 2.9 / 3.4 / 3.9 ms; 1,452 req/s, p95 18 ms | 2.4 / 2.9 / 3.3 ms; 1,525 req/s, p95 16 ms |
| balance, past round | 2.7 / 3.1 / 4.0 ms; 1,687 req/s, p95 14 ms | 2.1 / 2.4 / 3.1 ms; 1,571 req/s, p95 15 ms |
| history, busiest account | 6.1 / 8.3 / 8.8 ms; 755 req/s, p95 38 ms | 5.3 / 7.0 / 8.4 ms; 644 req/s, p95 43 ms |
| history, typical account | 1.8 / 3.3 / 3.8 ms; 1,867 req/s, p95 16 ms | 1.6 / 3.3 / 4.1 ms; 1,851 req/s, p95 14 ms |
| holders, native, latest | 3.3 / 5.7 / 8.6 ms; 875 req/s, p95 30 ms | 1.9 / 2.4 / 2.8 ms; 1,754 req/s, p95 13 ms |
| holders, native, past round | 9.1 / 11.7 / 15.3 ms; 181 req/s, p95 119 ms | 6.4 / 7.2 / 7.4 ms; 364 req/s, p95 68 ms |
| holders, busiest ASA, latest | 2.0 / 2.5 / 3.4 ms; 1,792 req/s, p95 14 ms | 1.2 / 1.4 / 1.6 ms; 3,097 req/s, p95 8 ms |
| holders, busiest ARC-200, latest | 2.2 / 2.6 / 3.4 ms; 1,441 req/s, p95 18 ms | 1.4 / 1.6 / 2.1 ms; 2,615 req/s, p95 9 ms |
| token info | 0.7 / 0.8 / 0.8 ms; 6,661 req/s, p95 4 ms | 0.5 / 0.5 / 0.6 ms; 8,075 req/s, p95 3 ms |

