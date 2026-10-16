# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-16 at commit `0c834a11`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,021,877 to 66,031,876) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 60 rounds/s | 195 rounds/s |
| Effects written | 1,503,640 (8,997/s) | 1,503,640 (29,345/s) |
| Time for the range | 167.1 s | 51.2 s |
| Stored size | 224.9 MiB | 967.4 MiB |
| Per round | 23.03 KiB | 99.06 KiB |
| Per effect | 156 bytes | 670 bytes |
| If all 66 M MainNet rounds were like these | 1.4 TiB | 6.1 TiB |

Block download is the same for both and not part of the table: 124 rounds/s from the public archival nodes (561,261 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 114.2 MiB, effects_by_token_bucket 98.0 MiB, effect_buckets_by_account_token 4.6 MiB, holdings_by_token 2.4 MiB, accounts_touched_by_token 2.4 MiB
- PostgreSQL 17.11: effects_by_account_token 535.9 MiB, effects_by_token_bucket 398.9 MiB, holdings_by_token 9.8 MiB, accounts_touched_by_token 9.8 MiB, holdings_by_account 8.8 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 3 ARC-200 tokens, 10,682 ASAs. 5,828 of those ASAs were created inside the range, 2 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 1,278,218 | 5,594 | 16,317 |
| asa:127746157 |  | 53,685 | 0 | 1 |
| asa:31566704 |  | 34,374 | 488 | 6,155 |
| asa:127745593 |  | 30,390 | 0 | 1 |
| asa:127746786 |  | 19,455 | 0 | 1 |
| asa:849191641 |  | 14,423 | 754 | 4,076 |
| asa:3145862805 |  | 10,819 | 1,627 | 1,628 |
| asa:1284444444 |  | 5,355 | 28 | 49 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:452399768 |  | 1,570 | 70 | 90 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.3 / 2.6 ms; 3,973 req/s, p95 7 ms | 0.4 / 0.5 / 0.6 ms; 7,289 req/s, p95 3 ms |
| account holdings | 1.9 / 4.1 / 6.2 ms; 1,817 req/s, p95 16 ms | 1.3 / 1.9 / 2.9 ms; 2,603 req/s, p95 11 ms |
| balance, latest | 3.2 / 5.3 / 6.8 ms; 1,317 req/s, p95 23 ms | 2.0 / 2.6 / 3.8 ms; 1,637 req/s, p95 16 ms |
| balance, past round | 2.9 / 3.6 / 4.7 ms; 1,492 req/s, p95 22 ms | 1.6 / 2.2 / 2.5 ms; 2,172 req/s, p95 12 ms |
| history, busiest account | 4.2 / 8.1 / 12.4 ms; 631 req/s, p95 48 ms | 4.1 / 5.6 / 6.5 ms; 937 req/s, p95 29 ms |
| history, typical account | 1.8 / 2.2 / 2.9 ms; 2,870 req/s, p95 8 ms | 1.7 / 2.3 / 3.4 ms; 2,256 req/s, p95 13 ms |
| holders, native, latest | 13.9 / 15.4 / 16.4 ms; 274 req/s, p95 76 ms | 2.1 / 3.1 / 4.2 ms; 1,382 req/s, p95 18 ms |
| holders, native, past round | 43.5 / 50.0 / 55.8 ms; 53 req/s, p95 352 ms | 7.2 / 8.1 / 10.0 ms; 317 req/s, p95 78 ms |
| holders, busiest ASA, latest | 2.1 / 2.8 / 3.9 ms; 1,941 req/s, p95 13 ms | 0.9 / 1.0 / 1.5 ms; 3,867 req/s, p95 7 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.3 / 2.6 ms; 1,812 req/s, p95 14 ms | 0.9 / 1.1 / 1.9 ms; 3,767 req/s, p95 6 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 7,817 req/s, p95 3 ms | 0.4 / 0.5 / 0.5 ms; 10,070 req/s, p95 3 ms |

## voi

Measured on 2026-10-16 at commit `0c834a11`: the 100,000 newest rounds of voi (chain rounds 23,518,269 to 23,618,268) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 2,260 rounds/s | 7,716 rounds/s |
| Effects written | 89,110 (2,014/s) | 89,110 (6,876/s) |
| Time for the range | 44.2 s | 13.0 s |
| Stored size | 10.1 MiB | 54.8 MiB |
| Per round | 0.10 KiB | 0.56 KiB |
| Per effect | 119 bytes | 645 bytes |

Block download is the same for both and not part of the table: 95 rounds/s from the public archival nodes (28,612 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 5.9 MiB, effects_by_token_bucket 4.1 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_account 0.0 MiB, holdings_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 30.4 MiB, effects_by_token_bucket 23.8 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 14 ARC-200 tokens, 4 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 85,588 | 146 | 173 |
| asa:586896 |  | 2,142 | 1 | 2 |
| arc200:390001 |  | 428 | 4 | 12 |
| arc200:395614 |  | 232 | 2 | 10 |
| arc200:47138068 |  | 158 | 9 | 11 |
| asa:302190 |  | 90 | 2 | 5 |
| arc200:300279 |  | 80 | 19 | 22 |
| arc200:404004 |  | 74 | 17 | 18 |
| arc200:411311 |  | 74 | 17 | 18 |
| arc200:416000 |  | 74 | 17 | 18 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.1 / 1.2 ms; 4,092 req/s, p95 7 ms | 0.4 / 0.5 / 0.6 ms; 8,158 req/s, p95 3 ms |
| account holdings | 1.7 / 2.1 / 3.2 ms; 1,981 req/s, p95 15 ms | 1.1 / 1.4 / 2.1 ms; 3,098 req/s, p95 8 ms |
| balance, latest | 3.0 / 3.5 / 4.3 ms; 1,417 req/s, p95 18 ms | 1.6 / 2.1 / 2.6 ms; 2,081 req/s, p95 12 ms |
| balance, past round | 2.9 / 3.4 / 3.7 ms; 1,629 req/s, p95 15 ms | 1.4 / 1.8 / 2.2 ms; 2,335 req/s, p95 10 ms |
| history, busiest account | 4.4 / 8.7 / 10.7 ms; 667 req/s, p95 45 ms | 3.9 / 5.6 / 6.5 ms; 1,037 req/s, p95 27 ms |
| history, typical account | 1.8 / 2.1 / 2.4 ms; 3,067 req/s, p95 9 ms | 1.1 / 1.5 / 2.5 ms; 2,927 req/s, p95 10 ms |
| holders, native, latest | 5.3 / 7.9 / 9.4 ms; 556 req/s, p95 45 ms | 1.8 / 2.3 / 2.7 ms; 1,651 req/s, p95 16 ms |
| holders, native, past round | 16.6 / 19.8 / 23.8 ms; 97 req/s, p95 204 ms | 5.8 / 6.7 / 7.4 ms; 375 req/s, p95 65 ms |
| holders, busiest ASA, latest | 2.0 / 2.3 / 2.8 ms; 1,850 req/s, p95 13 ms | 0.8 / 0.9 / 1.0 ms; 4,242 req/s, p95 6 ms |
| holders, busiest ARC-200, latest | 2.2 / 2.6 / 3.1 ms; 1,453 req/s, p95 17 ms | 0.9 / 1.1 / 1.5 ms; 3,614 req/s, p95 7 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 7,107 req/s, p95 4 ms | 0.2 / 0.3 / 0.4 ms; 14,943 req/s, p95 2 ms |

