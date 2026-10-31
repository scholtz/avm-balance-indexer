# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-31 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,492,655 to 66,502,654) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 236 rounds/s | 236 rounds/s |
| Effects written | 464,125 (10,960/s) | 464,125 (10,964/s) |
| Time for the range | 42.3 s | 42.3 s |
| Stored size | 83.3 MiB | 324.7 MiB |
| Per round | 8.54 KiB | 33.25 KiB |
| Per effect | 183 bytes | 712 bytes |
| If all 66 M MainNet rounds were like these | 0.5 TiB | 2.0 TiB |

Block download is the same for both and not part of the table: 192 rounds/s from the public archival nodes (123,932 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 40.9 MiB, effects_by_token_bucket 30.4 MiB, effect_buckets_by_account_token 4.4 MiB, holdings_by_token 2.3 MiB, accounts_touched_by_token 2.2 MiB
- PostgreSQL 17.11: effects_by_account_token 168.5 MiB, effects_by_token_bucket 124.8 MiB, accounts_touched_by_token 9.3 MiB, holdings_by_token 9.3 MiB, holdings_by_account 8.5 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 1 ARC-200 tokens, 10,655 ASAs. 5,598 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 336,832 | 5,865 | 15,023 |
| asa:31566704 |  | 40,288 | 458 | 6,240 |
| asa:849191641 |  | 35,532 | 2,286 | 7,125 |
| asa:1284444444 |  | 5,342 | 22 | 38 |
| asa:3737581431 |  | 4,800 | 23 | 24 |
| asa:2543151056 |  | 1,500 | 386 | 387 |
| asa:796425061 |  | 1,372 | 32 | 92 |
| asa:2726252423 |  | 1,107 | 41 | 62 |
| asa:3214347764 |  | 1,026 | 30 | 41 |
| asa:2155822658 |  | 1,001 | 9 | 27 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.6 / 0.9 / 2.4 ms; 6,453 req/s, p95 5 ms | 0.8 / 1.0 / 1.9 ms; 3,190 req/s, p95 8 ms |
| account holdings | 1.2 / 2.3 / 4.0 ms; 3,190 req/s, p95 10 ms | 2.1 / 3.3 / 4.6 ms; 1,481 req/s, p95 16 ms |
| balance, latest | 1.6 / 2.0 / 2.8 ms; 2,683 req/s, p95 10 ms | 3.3 / 5.0 / 6.1 ms; 1,034 req/s, p95 23 ms |
| balance, past round | 1.5 / 1.8 / 2.8 ms; 3,168 req/s, p95 8 ms | 2.7 / 3.8 / 4.6 ms; 1,253 req/s, p95 20 ms |
| history, busiest account | 2.8 / 5.0 / 6.8 ms; 1,085 req/s, p95 30 ms | 6.3 / 8.5 / 10.7 ms; 733 req/s, p95 32 ms |
| history, typical account | 0.9 / 1.1 / 2.2 ms; 5,611 req/s, p95 5 ms | 2.0 / 2.5 / 3.1 ms; 1,482 req/s, p95 19 ms |
| holders, native, latest | 4.8 / 6.2 / 7.2 ms; 666 req/s, p95 35 ms | 3.2 / 4.1 / 4.8 ms; 723 req/s, p95 36 ms |
| holders, native, past round | 13.2 / 15.3 / 17.6 ms; 148 req/s, p95 135 ms | 10.4 / 11.6 / 12.3 ms; 231 req/s, p95 105 ms |
| holders, busiest ASA, latest | 24.0 / 26.4 / 27.0 ms; 196 req/s, p95 94 ms | 3.1 / 3.7 / 4.3 ms; 752 req/s, p95 30 ms |
| holders, busiest ARC-200, latest | 0.8 / 1.1 / 1.3 ms; 4,554 req/s, p95 6 ms | 1.6 / 1.9 / 2.2 ms; 1,946 req/s, p95 13 ms |
| token info | 0.3 / 0.3 / 0.4 ms; 20,730 req/s, p95 1 ms | 0.6 / 0.7 / 0.8 ms; 4,947 req/s, p95 6 ms |

## voi

Measured on 2026-10-31 at commit `9e2e5bda`: the 100,000 newest rounds of voi (chain rounds 23,984,803 to 24,084,802) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 6,175 rounds/s | 5,794 rounds/s |
| Effects written | 40,742 (2,516/s) | 40,742 (2,360/s) |
| Time for the range | 16.2 s | 17.3 s |
| Stored size | 3.3 MiB | 25.8 MiB |
| Per round | 0.03 KiB | 0.26 KiB |
| Per effect | 86 bytes | 663 bytes |

Block download is the same for both and not part of the table: 36 rounds/s from the public archival nodes (7,573 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.9 MiB, effects_by_token_bucket 1.3 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_account 0.0 MiB, holdings_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 15.4 MiB, effects_by_token_bucket 9.9 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 27 ARC-200 tokens, 4 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 37,721 | 139 | 179 |
| asa:586896 |  | 1,558 | 1 | 2 |
| arc200:390001 |  | 436 | 9 | 12 |
| arc200:395614 |  | 236 | 3 | 11 |
| arc200:47138068 |  | 119 | 4 | 10 |
| arc200:300279 |  | 110 | 16 | 20 |
| arc200:420069 |  | 100 | 8 | 15 |
| arc200:404004 |  | 72 | 16 | 18 |
| arc200:411311 |  | 70 | 15 | 16 |
| arc200:416000 |  | 70 | 15 | 16 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.6 / 0.7 / 0.8 ms; 9,065 req/s, p95 3 ms | 0.8 / 1.0 / 1.7 ms; 3,340 req/s, p95 8 ms |
| account holdings | 1.0 / 1.4 / 1.9 ms; 3,669 req/s, p95 8 ms | 1.8 / 2.7 / 3.3 ms; 1,705 req/s, p95 14 ms |
| balance, latest | 1.5 / 1.9 / 2.3 ms; 3,526 req/s, p95 7 ms | 2.7 / 3.4 / 3.8 ms; 1,166 req/s, p95 22 ms |
| balance, past round | 1.5 / 1.9 / 2.6 ms; 3,388 req/s, p95 7 ms | 2.3 / 2.9 / 3.4 ms; 1,362 req/s, p95 18 ms |
| history, busiest account | 4.1 / 5.4 / 6.8 ms; 933 req/s, p95 34 ms | 5.8 / 8.4 / 9.2 ms; 764 req/s, p95 31 ms |
| history, typical account | 1.0 / 1.7 / 3.1 ms; 3,480 req/s, p95 9 ms | 1.8 / 2.9 / 4.2 ms; 1,609 req/s, p95 16 ms |
| holders, native, latest | 2.6 / 3.5 / 4.3 ms; 1,126 req/s, p95 23 ms | 2.9 / 3.8 / 4.1 ms; 924 req/s, p95 26 ms |
| holders, native, past round | 6.6 / 8.7 / 9.6 ms; 200 req/s, p95 111 ms | 9.5 / 12.5 / 13.6 ms; 234 req/s, p95 104 ms |
| holders, busiest ASA, latest | 0.8 / 1.0 / 1.2 ms; 4,572 req/s, p95 6 ms | 1.6 / 1.9 / 2.0 ms; 2,099 req/s, p95 11 ms |
| holders, busiest ARC-200, latest | 0.9 / 1.1 / 1.8 ms; 3,493 req/s, p95 8 ms | 1.8 / 2.0 / 2.4 ms; 1,867 req/s, p95 12 ms |
| token info | 0.2 / 0.3 / 0.4 ms; 21,337 req/s, p95 1 ms | 0.6 / 0.6 / 0.7 ms; 5,653 req/s, p95 5 ms |

