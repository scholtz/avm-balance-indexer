# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-19 at commit `ee61ce36`: the 10,000 newest rounds of algorand-mainnet (chain rounds 67,089,813 to 67,099,812) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 100 rounds/s | 249 rounds/s |
| Effects written | 646,580 (6,476/s) | 646,580 (16,130/s) |
| Time for the range | 99.8 s | 40.1 s |
| Stored size | 111.7 MiB | 438.0 MiB |
| Per round | 11.44 KiB | 44.86 KiB |
| Per effect | 176 bytes | 690 bytes |
| If all 66 M MainNet rounds were like these | 0.7 TiB | 2.8 TiB |

Block download is the same for both and not part of the table: 318 rounds/s from the public archival nodes (189,480 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 55.8 MiB, effects_by_token_bucket 44.6 MiB, effect_buckets_by_account_token 4.5 MiB, holdings_by_token 2.1 MiB, accounts_touched_by_token 2.1 MiB
- PostgreSQL 17.12: effects_by_account_token 232.2 MiB, effects_by_token_bucket 174.2 MiB, accounts_touched_by_token 10.0 MiB, holdings_by_token 10.0 MiB, holdings_by_account 9.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 3 ARC-200 tokens, 7,052 ASAs. 622 of those ASAs were created inside the range, 1 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 435,146 | 3,508 | 15,411 |
| asa:849191641 |  | 57,034 | 3,294 | 9,544 |
| asa:127746157 |  | 33,873 | 0 | 1 |
| asa:31566704 |  | 20,790 | 361 | 4,215 |
| asa:127745593 |  | 19,313 | 0 | 1 |
| asa:3145862805 |  | 18,042 | 2,756 | 2,757 |
| asa:127746786 |  | 11,540 | 0 | 1 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,334 | 22 | 35 |
| asa:386192725 |  | 1,694 | 21 | 119 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.3 / 2.7 / 5.0 ms; 2,805 req/s, p95 11 ms | 0.8 / 1.0 / 1.5 ms; 3,132 req/s, p95 9 ms |
| account holdings | 2.0 / 3.6 / 4.3 ms; 1,643 req/s, p95 18 ms | 1.8 / 2.2 / 4.3 ms; 1,567 req/s, p95 15 ms |
| balance, latest | 3.1 / 4.6 / 6.5 ms; 1,320 req/s, p95 19 ms | 2.9 / 3.6 / 3.9 ms; 1,060 req/s, p95 23 ms |
| balance, past round | 2.8 / 3.3 / 4.3 ms; 1,273 req/s, p95 19 ms | 2.3 / 3.2 / 3.9 ms; 1,143 req/s, p95 21 ms |
| history, busiest account | 8.1 / 15.3 / 19.7 ms; 636 req/s, p95 48 ms | 7.3 / 10.5 / 12.4 ms; 520 req/s, p95 63 ms |
| history, typical account | 1.7 / 1.9 / 3.0 ms; 2,628 req/s, p95 10 ms | 1.8 / 2.2 / 2.6 ms; 1,704 req/s, p95 14 ms |
| holders, native, latest | 17.9 / 21.1 / 24.4 ms; 236 req/s, p95 89 ms | 2.9 / 4.2 / 5.5 ms; 897 req/s, p95 26 ms |
| holders, native, past round | 34.6 / 44.1 / 47.7 ms; 61 req/s, p95 323 ms | 9.5 / 10.4 / 13.9 ms; 244 req/s, p95 96 ms |
| holders, busiest ASA, latest | 13.0 / 20.7 / 26.9 ms; 309 req/s, p95 66 ms | 2.6 / 3.2 / 3.4 ms; 940 req/s, p95 24 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.7 / 3.6 ms; 1,820 req/s, p95 13 ms | 1.6 / 1.9 / 2.1 ms; 2,033 req/s, p95 11 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 5,493 req/s, p95 5 ms | 0.6 / 0.7 / 0.8 ms; 4,668 req/s, p95 6 ms |

## voi

Measured on 2026-11-19 at commit `ee61ce36`: the 100,000 newest rounds of voi (chain rounds 24,579,212 to 24,679,211) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 2,431 rounds/s | 5,914 rounds/s |
| Effects written | 41,450 (1,008/s) | 41,450 (2,451/s) |
| Time for the range | 41.1 s | 16.9 s |
| Stored size | 2.5 MiB | 25.1 MiB |
| Per round | 0.03 KiB | 0.26 KiB |
| Per effect | 64 bytes | 634 bytes |

Block download is the same for both and not part of the table: 64 rounds/s from the public archival nodes (5,256 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.5 MiB, effects_by_token_bucket 1.0 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_account 0.0 MiB, holdings_by_token 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 14.8 MiB, effects_by_token_bucket 9.7 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 15 ARC-200 tokens, 3 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 40,938 | 134 | 165 |
| asa:586896 |  | 116 | 1 | 2 |
| arc200:300279 |  | 64 | 14 | 15 |
| arc200:404004 |  | 64 | 14 | 15 |
| arc200:411311 |  | 64 | 14 | 15 |
| arc200:416000 |  | 64 | 14 | 15 |
| arc200:420019 |  | 64 | 14 | 15 |
| arc200:390001 |  | 14 | 3 | 5 |
| arc200:420069 |  | 12 | 3 | 6 |
| asa:302189 |  | 11 | 1 | 2 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.1 / 2.0 / 4.0 ms; 3,003 req/s, p95 10 ms | 0.8 / 1.1 / 2.7 ms; 3,156 req/s, p95 8 ms |
| account holdings | 1.7 / 2.1 / 2.7 ms; 1,785 req/s, p95 15 ms | 1.8 / 2.2 / 3.6 ms; 1,663 req/s, p95 15 ms |
| balance, latest | 2.8 / 3.4 / 4.4 ms; 1,506 req/s, p95 15 ms | 2.8 / 3.6 / 4.6 ms; 1,112 req/s, p95 21 ms |
| balance, past round | 2.8 / 3.2 / 3.5 ms; 1,414 req/s, p95 17 ms | 2.3 / 3.1 / 3.9 ms; 1,230 req/s, p95 19 ms |
| history, busiest account | 8.2 / 11.8 / 16.6 ms; 485 req/s, p95 91 ms | 7.4 / 11.4 / 12.5 ms; 501 req/s, p95 63 ms |
| history, typical account | 1.7 / 2.7 / 3.3 ms; 2,380 req/s, p95 11 ms | 1.7 / 2.6 / 4.1 ms; 1,693 req/s, p95 14 ms |
| holders, native, latest | 4.2 / 5.9 / 8.5 ms; 609 req/s, p95 43 ms | 2.7 / 3.7 / 5.3 ms; 1,015 req/s, p95 23 ms |
| holders, native, past round | 12.7 / 16.3 / 19.9 ms; 167 req/s, p95 130 ms | 7.8 / 8.7 / 10.5 ms; 288 req/s, p95 82 ms |
| holders, busiest ASA, latest | 2.0 / 2.4 / 2.9 ms; 1,945 req/s, p95 12 ms | 1.6 / 1.7 / 1.7 ms; 1,996 req/s, p95 11 ms |
| holders, busiest ARC-200, latest | 2.3 / 3.0 / 3.6 ms; 1,384 req/s, p95 18 ms | 2.1 / 3.3 / 4.7 ms; 1,703 req/s, p95 14 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 5,625 req/s, p95 5 ms | 0.6 / 0.6 / 0.7 ms; 4,960 req/s, p95 6 ms |

