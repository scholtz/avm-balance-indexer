# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-04 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,618,624 to 66,628,623) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 141 rounds/s | 278 rounds/s |
| Effects written | 541,134 (7,635/s) | 541,134 (15,032/s) |
| Time for the range | 70.9 s | 36.0 s |
| Stored size | 95.2 MiB | 371.9 MiB |
| Per round | 9.75 KiB | 38.08 KiB |
| Per effect | 178 bytes | 696 bytes |
| If all 66 M MainNet rounds were like these | 0.6 TiB | 2.3 TiB |

Block download is the same for both and not part of the table: 155 rounds/s from the public archival nodes (145,690 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 48.7 MiB, effects_by_token_bucket 36.1 MiB, effect_buckets_by_account_token 4.1 MiB, holdings_by_token 2.0 MiB, accounts_touched_by_token 1.9 MiB
- PostgreSQL 17.11: effects_by_account_token 197.0 MiB, effects_by_token_bucket 146.4 MiB, holdings_by_token 9.2 MiB, accounts_touched_by_token 9.2 MiB, holdings_by_account 8.3 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 3,929 ASAs. 2,139 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 369,930 | 4,309 | 16,468 |
| asa:31566704 |  | 73,143 | 476 | 3,841 |
| asa:849191641 |  | 44,459 | 2,635 | 8,861 |
| asa:3737581431 |  | 4,800 | 23 | 24 |
| asa:1284444444 |  | 4,603 | 12 | 26 |
| asa:3203964481 |  | 1,837 | 13 | 84 |
| asa:2543151056 |  | 1,570 | 400 | 401 |
| asa:2726252423 |  | 1,404 | 33 | 60 |
| asa:452399768 |  | 1,326 | 60 | 82 |
| asa:971384592 |  | 1,312 | 32 | 47 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.2 / 2.0 / 3.1 ms; 4,015 req/s, p95 7 ms | 0.8 / 1.0 / 1.5 ms; 3,351 req/s, p95 7 ms |
| account holdings | 2.3 / 4.7 / 5.7 ms; 1,652 req/s, p95 17 ms | 2.2 / 3.6 / 4.5 ms; 1,464 req/s, p95 18 ms |
| balance, latest | 2.8 / 4.3 / 6.0 ms; 1,596 req/s, p95 18 ms | 2.7 / 3.7 / 4.3 ms; 1,109 req/s, p95 23 ms |
| balance, past round | 2.5 / 4.2 / 8.8 ms; 1,771 req/s, p95 16 ms | 2.2 / 2.9 / 3.7 ms; 1,346 req/s, p95 17 ms |
| history, busiest account | 5.7 / 9.5 / 15.5 ms; 766 req/s, p95 39 ms | 5.5 / 8.1 / 9.5 ms; 796 req/s, p95 29 ms |
| history, typical account | 1.5 / 1.6 / 2.3 ms; 4,205 req/s, p95 6 ms | 1.9 / 2.1 / 2.5 ms; 1,548 req/s, p95 18 ms |
| holders, native, latest | 13.4 / 14.6 / 17.3 ms; 294 req/s, p95 73 ms | 2.8 / 3.7 / 5.0 ms; 908 req/s, p95 24 ms |
| holders, native, past round | 24.8 / 28.6 / 30.6 ms; 76 req/s, p95 254 ms | 8.8 / 9.7 / 10.9 ms; 240 req/s, p95 99 ms |
| holders, busiest ASA, latest | 28.8 / 32.0 / 34.2 ms; 171 req/s, p95 106 ms | 2.8 / 3.6 / 4.7 ms; 871 req/s, p95 25 ms |
| token info | 0.6 / 0.7 / 0.7 ms; 9,605 req/s, p95 3 ms | 0.6 / 0.7 / 1.3 ms; 4,651 req/s, p95 6 ms |

## voi

Measured on 2026-11-04 at commit `9e2e5bda`: the 100,000 newest rounds of voi (chain rounds 24,110,665 to 24,210,664) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 5,017 rounds/s | 7,276 rounds/s |
| Effects written | 28,764 (1,443/s) | 28,764 (2,093/s) |
| Time for the range | 19.9 s | 13.7 s |
| Stored size | 2.4 MiB | 19.7 MiB |
| Per round | 0.02 KiB | 0.20 KiB |
| Per effect | 88 bytes | 715 bytes |

Block download is the same for both and not part of the table: 30 rounds/s from the public archival nodes (5,972 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.4 MiB, effects_by_token_bucket 0.9 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 12.1 MiB, effects_by_token_bucket 7.0 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 26 ARC-200 tokens, 6 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 26,038 | 51 | 105 |
| arc200:390001 |  | 612 | 11 | 24 |
| asa:586896 |  | 492 | 1 | 2 |
| arc200:300279 |  | 272 | 26 | 30 |
| arc200:395614 |  | 228 | 5 | 13 |
| arc200:420069 |  | 162 | 11 | 18 |
| arc200:47138068 |  | 120 | 5 | 11 |
| arc200:40153155 |  | 116 | 4 | 15 |
| arc200:412682 |  | 100 | 9 | 11 |
| arc200:404004 |  | 76 | 18 | 21 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.3 / 2.8 ms; 4,031 req/s, p95 7 ms | 0.8 / 1.1 / 2.2 ms; 3,570 req/s, p95 7 ms |
| account holdings | 1.9 / 2.6 / 4.3 ms; 2,199 req/s, p95 13 ms | 2.0 / 3.6 / 4.5 ms; 1,526 req/s, p95 16 ms |
| balance, latest | 2.7 / 3.4 / 4.2 ms; 1,808 req/s, p95 14 ms | 2.6 / 3.8 / 4.7 ms; 1,190 req/s, p95 21 ms |
| balance, past round | 2.5 / 2.9 / 3.5 ms; 1,819 req/s, p95 14 ms | 2.2 / 2.8 / 3.3 ms; 1,439 req/s, p95 17 ms |
| history, busiest account | 6.7 / 8.4 / 9.1 ms; 846 req/s, p95 35 ms | 5.8 / 8.4 / 9.0 ms; 775 req/s, p95 30 ms |
| history, typical account | 1.6 / 2.8 / 3.3 ms; 2,387 req/s, p95 13 ms | 1.7 / 3.3 / 4.4 ms; 1,543 req/s, p95 17 ms |
| holders, native, latest | 3.0 / 4.0 / 4.8 ms; 978 req/s, p95 25 ms | 2.4 / 3.0 / 3.5 ms; 1,311 req/s, p95 17 ms |
| holders, native, past round | 8.6 / 11.1 / 14.1 ms; 195 req/s, p95 106 ms | 7.7 / 8.5 / 9.6 ms; 272 req/s, p95 87 ms |
| holders, busiest ASA, latest | 1.6 / 2.0 / 2.9 ms; 2,268 req/s, p95 11 ms | 1.5 / 1.7 / 2.1 ms; 2,231 req/s, p95 10 ms |
| holders, busiest ARC-200, latest | 1.9 / 2.2 / 3.0 ms; 1,730 req/s, p95 15 ms | 1.7 / 2.0 / 2.4 ms; 1,835 req/s, p95 13 ms |
| token info | 0.6 / 0.7 / 0.7 ms; 9,113 req/s, p95 3 ms | 0.6 / 0.6 / 0.7 ms; 5,540 req/s, p95 5 ms |

