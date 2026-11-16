# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-16 at commit `1f8807ab`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,995,707 to 67,005,706) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 86 rounds/s | 279 rounds/s |
| Effects written | 763,585 (6,598/s) | 763,585 (21,334/s) |
| Time for the range | 115.7 s | 35.8 s |
| Stored size | 131.9 MiB | 516.2 MiB |
| Per round | 13.50 KiB | 52.86 KiB |
| Per effect | 177 bytes | 694 bytes |
| If all 66 M MainNet rounds were like these | 0.8 TiB | 3.2 TiB |

Block download is the same for both and not part of the table: 286 rounds/s from the public archival nodes (230,194 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 65.0 MiB, effects_by_token_bucket 53.0 MiB, effect_buckets_by_account_token 5.5 MiB, accounts_touched_by_token 2.8 MiB, holdings_by_token 2.8 MiB
- PostgreSQL 17.12: effects_by_account_token 273.5 MiB, effects_by_token_bucket 204.2 MiB, holdings_by_token 12.3 MiB, accounts_touched_by_token 12.2 MiB, holdings_by_account 11.3 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 5 ARC-200 tokens, 10,555 ASAs. 2,631 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 522,954 | 7,532 | 17,264 |
| asa:127746157 |  | 54,720 | 0 | 1 |
| asa:849191641 |  | 53,588 | 3,208 | 9,490 |
| asa:127745593 |  | 31,610 | 0 | 1 |
| asa:31566704 |  | 27,270 | 357 | 3,979 |
| asa:127746786 |  | 17,133 | 0 | 1 |
| asa:3737581431 |  | 4,636 | 22 | 24 |
| asa:1284444444 |  | 4,234 | 15 | 33 |
| asa:3203964481 |  | 2,815 | 45 | 86 |
| asa:2543151056 |  | 1,604 | 409 | 410 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.6 / 3.8 ms; 3,020 req/s, p95 9 ms | 0.5 / 0.7 / 1.0 ms; 4,897 req/s, p95 5 ms |
| account holdings | 2.0 / 3.9 / 5.5 ms; 1,487 req/s, p95 20 ms | 1.5 / 2.3 / 3.6 ms; 2,241 req/s, p95 10 ms |
| balance, latest | 2.9 / 4.8 / 9.5 ms; 1,469 req/s, p95 18 ms | 2.1 / 2.5 / 4.0 ms; 1,586 req/s, p95 15 ms |
| balance, past round | 2.5 / 3.0 / 4.0 ms; 1,852 req/s, p95 14 ms | 1.6 / 2.2 / 2.9 ms; 1,765 req/s, p95 13 ms |
| history, busiest account | 6.0 / 11.2 / 17.9 ms; 686 req/s, p95 42 ms | 4.5 / 6.5 / 7.6 ms; 734 req/s, p95 38 ms |
| history, typical account | 1.6 / 1.8 / 2.4 ms; 2,471 req/s, p95 11 ms | 1.5 / 2.0 / 2.6 ms; 2,226 req/s, p95 11 ms |
| holders, native, latest | 9.9 / 12.0 / 13.7 ms; 330 req/s, p95 68 ms | 2.1 / 2.9 / 3.5 ms; 1,220 req/s, p95 20 ms |
| holders, native, past round | 43.5 / 52.9 / 70.9 ms; 57 req/s, p95 340 ms | 7.3 / 8.0 / 9.2 ms; 307 req/s, p95 75 ms |
| holders, busiest ASA, latest | 1.9 / 2.3 / 3.2 ms; 1,876 req/s, p95 14 ms | 1.1 / 1.4 / 2.2 ms; 2,906 req/s, p95 8 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.4 / 2.5 ms; 1,552 req/s, p95 17 ms | 1.2 / 1.3 / 2.3 ms; 2,883 req/s, p95 8 ms |
| token info | 0.6 / 0.7 / 1.2 ms; 5,232 req/s, p95 6 ms | 0.4 / 0.5 / 0.6 ms; 6,464 req/s, p95 4 ms |

## voi

Measured on 2026-11-16 at commit `1f8807ab`: the 100,000 newest rounds of voi (chain rounds 24,486,164 to 24,586,163) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 6,154 rounds/s | 13,725 rounds/s |
| Effects written | 16,587 (1,021/s) | 16,587 (2,277/s) |
| Time for the range | 16.3 s | 7.3 s |
| Stored size | 1.8 MiB | 13.1 MiB |
| Per round | 0.02 KiB | 0.13 KiB |
| Per effect | 114 bytes | 826 bytes |

Block download is the same for both and not part of the table: 65 rounds/s from the public archival nodes (5,963 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.0 MiB, effects_by_token_bucket 0.7 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, holdings_by_account 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 8.4 MiB, effects_by_token_bucket 4.3 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 16 ARC-200 tokens, 4 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 15,447 | 62 | 98 |
| asa:586896 |  | 326 | 1 | 2 |
| arc200:420069 |  | 228 | 6 | 13 |
| arc200:390001 |  | 108 | 5 | 13 |
| arc200:300279 |  | 72 | 16 | 19 |
| arc200:404004 |  | 68 | 15 | 16 |
| arc200:411311 |  | 68 | 15 | 16 |
| arc200:416000 |  | 68 | 15 | 16 |
| arc200:420019 |  | 62 | 14 | 15 |
| arc200:395614 |  | 60 | 4 | 8 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.3 / 2.0 ms; 3,371 req/s, p95 9 ms | 0.6 / 0.7 / 2.1 ms; 4,839 req/s, p95 5 ms |
| account holdings | 1.7 / 2.2 / 3.5 ms; 1,910 req/s, p95 16 ms | 1.4 / 1.8 / 2.5 ms; 2,166 req/s, p95 11 ms |
| balance, latest | 2.7 / 2.9 / 3.7 ms; 1,536 req/s, p95 16 ms | 2.1 / 2.7 / 3.3 ms; 1,634 req/s, p95 15 ms |
| balance, past round | 2.5 / 2.9 / 3.7 ms; 1,714 req/s, p95 14 ms | 1.7 / 2.2 / 2.7 ms; 1,773 req/s, p95 13 ms |
| history, busiest account | 6.8 / 9.3 / 16.0 ms; 700 req/s, p95 42 ms | 4.8 / 6.7 / 7.3 ms; 746 req/s, p95 37 ms |
| history, typical account | 1.7 / 2.9 / 3.2 ms; 2,168 req/s, p95 13 ms | 1.5 / 2.7 / 4.0 ms; 2,163 req/s, p95 11 ms |
| holders, native, latest | 3.6 / 6.4 / 12.2 ms; 790 req/s, p95 33 ms | 1.8 / 2.5 / 3.2 ms; 1,623 req/s, p95 14 ms |
| holders, native, past round | 9.1 / 11.5 / 14.3 ms; 184 req/s, p95 113 ms | 6.2 / 6.9 / 8.2 ms; 356 req/s, p95 64 ms |
| holders, busiest ASA, latest | 2.1 / 3.0 / 4.0 ms; 1,695 req/s, p95 16 ms | 1.2 / 1.3 / 1.4 ms; 3,067 req/s, p95 7 ms |
| holders, busiest ARC-200, latest | 2.2 / 2.7 / 3.4 ms; 1,473 req/s, p95 16 ms | 1.3 / 1.5 / 1.9 ms; 2,656 req/s, p95 8 ms |
| token info | 0.7 / 0.8 / 0.8 ms; 7,161 req/s, p95 4 ms | 0.4 / 0.5 / 0.6 ms; 7,259 req/s, p95 4 ms |

