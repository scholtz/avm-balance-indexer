# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-14 at commit `b5f9ebc1`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,932,386 to 66,942,385) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 108 rounds/s | 223 rounds/s |
| Effects written | 1,005,637 (10,820/s) | 1,005,637 (22,464/s) |
| Time for the range | 92.9 s | 44.8 s |
| Stored size | 165.8 MiB | 666.6 MiB |
| Per round | 16.98 KiB | 68.26 KiB |
| Per effect | 170 bytes | 683 bytes |
| If all 66 M MainNet rounds were like these | 1.0 TiB | 4.2 TiB |

Block download is the same for both and not part of the table: 308 rounds/s from the public archival nodes (294,347 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 84.5 MiB, effects_by_token_bucket 66.6 MiB, effect_buckets_by_account_token 5.6 MiB, holdings_by_account 2.8 MiB, holdings_by_token 2.7 MiB
- PostgreSQL 17.12: effects_by_account_token 358.8 MiB, effects_by_token_bucket 269.8 MiB, accounts_touched_by_token 12.1 MiB, holdings_by_token 12.1 MiB, holdings_by_account 10.9 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 7,481 ASAs. 3,426 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 648,732 | 7,941 | 17,536 |
| asa:3145862805 |  | 94,387 | 10,903 | 10,904 |
| asa:127746157 |  | 54,322 | 0 | 1 |
| asa:31566704 |  | 52,511 | 342 | 4,738 |
| asa:849191641 |  | 49,139 | 2,718 | 8,815 |
| asa:127745593 |  | 33,499 | 0 | 1 |
| asa:127746786 |  | 15,634 | 0 | 1 |
| asa:3737581431 |  | 4,800 | 23 | 24 |
| asa:1284444444 |  | 4,237 | 21 | 35 |
| asa:1134696561 |  | 1,762 | 37 | 63 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 2.0 / 3.7 ms; 4,458 req/s, p95 6 ms | 0.7 / 0.8 / 1.0 ms; 4,127 req/s, p95 6 ms |
| account holdings | 1.4 / 3.8 / 6.6 ms; 2,621 req/s, p95 10 ms | 1.8 / 2.2 / 3.0 ms; 1,787 req/s, p95 14 ms |
| balance, latest | 2.2 / 3.9 / 6.1 ms; 2,028 req/s, p95 13 ms | 2.5 / 3.2 / 4.2 ms; 1,293 req/s, p95 18 ms |
| balance, past round | 2.0 / 2.7 / 3.3 ms; 2,358 req/s, p95 11 ms | 2.1 / 2.8 / 3.5 ms; 1,487 req/s, p95 16 ms |
| history, busiest account | 5.6 / 12.4 / 23.3 ms; 776 req/s, p95 42 ms | 4.8 / 7.8 / 9.3 ms; 728 req/s, p95 33 ms |
| history, typical account | 1.2 / 1.6 / 3.4 ms; 4,323 req/s, p95 6 ms | 1.8 / 2.2 / 2.9 ms; 2,004 req/s, p95 12 ms |
| holders, native, latest | 8.6 / 12.9 / 17.4 ms; 438 req/s, p95 47 ms | 2.7 / 3.5 / 4.7 ms; 939 req/s, p95 25 ms |
| holders, native, past round | 19.6 / 23.6 / 25.6 ms; 106 req/s, p95 184 ms | 8.4 / 9.3 / 10.7 ms; 280 req/s, p95 86 ms |
| holders, busiest ASA, latest | 3.0 / 4.2 / 8.1 ms; 760 req/s, p95 32 ms | 2.5 / 3.1 / 3.2 ms; 987 req/s, p95 22 ms |
| token info | 0.5 / 0.6 / 0.6 ms; 9,918 req/s, p95 3 ms | 0.6 / 0.7 / 0.8 ms; 5,360 req/s, p95 5 ms |

## voi

Measured on 2026-11-14 at commit `b5f9ebc1`: the 100,000 newest rounds of voi (chain rounds 24,423,068 to 24,523,067) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 5,167 rounds/s | 8,848 rounds/s |
| Effects written | 29,174 (1,507/s) | 29,174 (2,581/s) |
| Time for the range | 19.4 s | 11.3 s |
| Stored size | 2.5 MiB | 19.8 MiB |
| Per round | 0.03 KiB | 0.20 KiB |
| Per effect | 89 bytes | 710 bytes |

Block download is the same for both and not part of the table: 73 rounds/s from the public archival nodes (6,484 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.4 MiB, effects_by_token_bucket 1.0 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_account 0.0 MiB, holdings_by_token 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 12.1 MiB, effects_by_token_bucket 7.1 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 20 ARC-200 tokens, 9 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 27,872 | 126 | 166 |
| asa:586896 |  | 292 | 1 | 2 |
| arc200:420069 |  | 290 | 11 | 20 |
| arc200:390001 |  | 140 | 5 | 16 |
| arc200:300279 |  | 84 | 13 | 19 |
| arc200:404004 |  | 80 | 13 | 19 |
| arc200:411311 |  | 72 | 12 | 16 |
| arc200:416000 |  | 66 | 14 | 15 |
| arc200:420019 |  | 66 | 14 | 15 |
| arc200:47138068 |  | 50 | 4 | 10 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.7 / 0.9 / 1.7 ms; 4,404 req/s, p95 6 ms | 0.7 / 0.8 / 0.9 ms; 3,826 req/s, p95 7 ms |
| account holdings | 1.4 / 2.1 / 2.8 ms; 2,619 req/s, p95 11 ms | 1.7 / 2.2 / 3.1 ms; 1,985 req/s, p95 12 ms |
| balance, latest | 2.1 / 2.5 / 2.9 ms; 1,750 req/s, p95 16 ms | 2.4 / 2.9 / 3.2 ms; 1,430 req/s, p95 16 ms |
| balance, past round | 2.1 / 2.5 / 4.4 ms; 2,152 req/s, p95 11 ms | 2.1 / 2.8 / 3.6 ms; 1,521 req/s, p95 15 ms |
| history, busiest account | 5.7 / 7.9 / 10.9 ms; 687 req/s, p95 49 ms | 5.5 / 8.0 / 8.9 ms; 701 req/s, p95 38 ms |
| history, typical account | 1.3 / 2.1 / 3.1 ms; 3,498 req/s, p95 8 ms | 1.5 / 2.5 / 3.3 ms; 1,975 req/s, p95 12 ms |
| holders, native, latest | 4.1 / 5.6 / 8.7 ms; 666 req/s, p95 36 ms | 2.6 / 3.3 / 3.8 ms; 1,050 req/s, p95 21 ms |
| holders, native, past round | 12.1 / 16.6 / 19.6 ms; 140 req/s, p95 140 ms | 7.9 / 8.8 / 10.1 ms; 281 req/s, p95 83 ms |
| holders, busiest ASA, latest | 1.4 / 1.7 / 2.0 ms; 2,099 req/s, p95 15 ms | 1.4 / 1.5 / 2.1 ms; 2,796 req/s, p95 8 ms |
| holders, busiest ARC-200, latest | 1.6 / 2.3 / 3.9 ms; 1,940 req/s, p95 13 ms | 1.6 / 2.1 / 3.3 ms; 2,226 req/s, p95 10 ms |
| token info | 0.4 / 0.6 / 0.6 ms; 9,217 req/s, p95 3 ms | 0.5 / 0.5 / 0.6 ms; 6,858 req/s, p95 4 ms |

