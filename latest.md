# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-29 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,430,236 to 66,440,235) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 210 rounds/s | 246 rounds/s |
| Effects written | 624,094 (13,078/s) | 624,094 (15,358/s) |
| Time for the range | 47.7 s | 40.6 s |
| Stored size | 106.5 MiB | 423.7 MiB |
| Per round | 10.90 KiB | 43.38 KiB |
| Per effect | 175 bytes | 694 bytes |
| If all 66 M MainNet rounds were like these | 0.7 TiB | 2.7 TiB |

Block download is the same for both and not part of the table: 231 rounds/s from the public archival nodes (172,486 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 54.8 MiB, effects_by_token_bucket 40.5 MiB, effect_buckets_by_account_token 4.3 MiB, holdings_by_token 2.1 MiB, accounts_touched_by_token 2.0 MiB
- PostgreSQL 17.11: effects_by_account_token 225.2 MiB, effects_by_token_bucket 168.2 MiB, holdings_by_token 9.5 MiB, accounts_touched_by_token 9.5 MiB, holdings_by_account 8.7 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 5 ARC-200 tokens, 6,411 ASAs. 2,461 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 446,652 | 5,224 | 15,871 |
| asa:31566704 |  | 65,009 | 384 | 4,210 |
| asa:849191641 |  | 34,321 | 2,161 | 7,441 |
| asa:3145862805 |  | 13,409 | 1,903 | 1,904 |
| asa:3737581431 |  | 4,798 | 24 | 24 |
| asa:1284444444 |  | 4,777 | 23 | 38 |
| asa:3203964481 |  | 2,210 | 47 | 77 |
| asa:796425061 |  | 2,194 | 55 | 137 |
| asa:2537013734 |  | 1,848 | 46 | 68 |
| asa:452399768 |  | 1,795 | 74 | 91 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.6 / 0.9 / 1.6 ms; 6,533 req/s, p95 5 ms | 0.8 / 0.9 / 1.5 ms; 3,382 req/s, p95 7 ms |
| account holdings | 1.4 / 3.0 / 5.0 ms; 2,223 req/s, p95 18 ms | 2.0 / 2.6 / 3.4 ms; 1,415 req/s, p95 18 ms |
| balance, latest | 2.0 / 4.6 / 8.2 ms; 2,001 req/s, p95 14 ms | 3.2 / 5.6 / 6.6 ms; 1,107 req/s, p95 23 ms |
| balance, past round | 1.8 / 2.4 / 3.3 ms; 3,123 req/s, p95 9 ms | 2.5 / 3.4 / 4.0 ms; 1,287 req/s, p95 19 ms |
| history, busiest account | 3.0 / 6.3 / 10.0 ms; 913 req/s, p95 36 ms | 5.5 / 7.8 / 9.0 ms; 702 req/s, p95 38 ms |
| history, typical account | 0.8 / 1.1 / 2.3 ms; 5,687 req/s, p95 5 ms | 1.9 / 2.7 / 3.7 ms; 1,623 req/s, p95 17 ms |
| holders, native, latest | 6.1 / 7.6 / 10.7 ms; 584 req/s, p95 35 ms | 2.8 / 3.7 / 4.5 ms; 923 req/s, p95 25 ms |
| holders, native, past round | 14.2 / 17.7 / 19.6 ms; 143 req/s, p95 147 ms | 8.9 / 9.8 / 10.3 ms; 235 req/s, p95 100 ms |
| holders, busiest ASA, latest | 20.4 / 24.6 / 32.5 ms; 223 req/s, p95 81 ms | 2.9 / 3.6 / 4.8 ms; 779 req/s, p95 30 ms |
| holders, busiest ARC-200, latest | 0.8 / 1.2 / 2.1 ms; 3,584 req/s, p95 8 ms | 1.6 / 1.7 / 2.3 ms; 2,029 req/s, p95 12 ms |
| token info | 0.3 / 0.3 / 0.4 ms; 17,861 req/s, p95 1 ms | 0.6 / 0.9 / 1.5 ms; 5,183 req/s, p95 5 ms |

## voi

Measured on 2026-10-29 at commit `9e2e5bda`: the 100,000 newest rounds of voi (chain rounds 23,922,330 to 24,022,329) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 8,617 rounds/s | 7,509 rounds/s |
| Effects written | 27,131 (2,338/s) | 27,131 (2,037/s) |
| Time for the range | 11.6 s | 13.3 s |
| Stored size | 2.9 MiB | 19.3 MiB |
| Per round | 0.03 KiB | 0.20 KiB |
| Per effect | 111 bytes | 743 bytes |

Block download is the same for both and not part of the table: 31 rounds/s from the public archival nodes (7,417 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.6 MiB, effects_by_token_bucket 1.1 MiB, effect_buckets_by_account_token 0.1 MiB, holdings_by_token 0.0 MiB, holdings_by_account 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 11.8 MiB, effects_by_token_bucket 6.9 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 35 ARC-200 tokens, 6 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 23,941 | 150 | 201 |
| asa:586896 |  | 1,324 | 1 | 2 |
| arc200:390001 |  | 484 | 10 | 17 |
| arc200:40153155 |  | 286 | 6 | 22 |
| arc200:420069 |  | 110 | 14 | 22 |
| asa:40152679 |  | 100 | 4 | 9 |
| arc200:300279 |  | 86 | 20 | 24 |
| arc200:404004 |  | 84 | 20 | 26 |
| arc200:411311 |  | 66 | 17 | 18 |
| arc200:416000 |  | 66 | 17 | 18 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.6 / 1.0 / 1.2 ms; 8,060 req/s, p95 4 ms | 0.8 / 1.3 / 2.9 ms; 3,474 req/s, p95 8 ms |
| account holdings | 1.0 / 1.2 / 2.1 ms; 3,441 req/s, p95 8 ms | 1.9 / 2.5 / 3.2 ms; 1,689 req/s, p95 14 ms |
| balance, latest | 1.6 / 2.1 / 2.8 ms; 3,407 req/s, p95 7 ms | 2.8 / 3.6 / 4.3 ms; 1,173 req/s, p95 21 ms |
| balance, past round | 1.5 / 1.8 / 2.1 ms; 3,604 req/s, p95 6 ms | 2.7 / 3.4 / 3.7 ms; 1,311 req/s, p95 18 ms |
| history, busiest account | 4.1 / 5.3 / 5.9 ms; 921 req/s, p95 37 ms | 6.4 / 8.8 / 9.5 ms; 718 req/s, p95 32 ms |
| history, typical account | 1.1 / 1.5 / 2.3 ms; 3,722 req/s, p95 8 ms | 1.8 / 2.4 / 3.0 ms; 1,569 req/s, p95 17 ms |
| holders, native, latest | 2.2 / 3.3 / 4.1 ms; 923 req/s, p95 33 ms | 2.9 / 3.7 / 4.4 ms; 956 req/s, p95 23 ms |
| holders, native, past round | 9.3 / 11.5 / 13.7 ms; 249 req/s, p95 79 ms | 8.6 / 9.5 / 11.7 ms; 248 req/s, p95 95 ms |
| holders, busiest ASA, latest | 0.8 / 1.0 / 1.3 ms; 4,169 req/s, p95 6 ms | 1.7 / 1.8 / 1.9 ms; 2,041 req/s, p95 12 ms |
| holders, busiest ARC-200, latest | 1.0 / 1.3 / 2.0 ms; 2,993 req/s, p95 9 ms | 1.8 / 2.1 / 2.7 ms; 1,782 req/s, p95 13 ms |
| token info | 0.3 / 0.3 / 0.4 ms; 18,363 req/s, p95 2 ms | 0.6 / 0.7 / 0.8 ms; 5,695 req/s, p95 5 ms |

