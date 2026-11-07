# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-07 at commit `6396135a`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,712,216 to 66,722,215) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 116 rounds/s | 332 rounds/s |
| Effects written | 441,976 (5,113/s) | 441,976 (14,676/s) |
| Time for the range | 86.4 s | 30.1 s |
| Stored size | 80.3 MiB | 307.3 MiB |
| Per round | 8.22 KiB | 31.47 KiB |
| Per effect | 184 bytes | 703 bytes |
| If all 66 M MainNet rounds were like these | 0.5 TiB | 1.9 TiB |

Block download is the same for both and not part of the table: 332 rounds/s from the public archival nodes (116,888 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 40.1 MiB, effects_by_token_bucket 29.7 MiB, effect_buckets_by_account_token 4.0 MiB, holdings_by_token 2.0 MiB, accounts_touched_by_token 2.0 MiB
- PostgreSQL 17.11: effects_by_account_token 161.2 MiB, effects_by_token_bucket 119.5 MiB, holdings_by_token 8.2 MiB, accounts_touched_by_token 8.2 MiB, holdings_by_account 7.5 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 6,867 ASAs. 3,217 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 305,418 | 4,562 | 14,586 |
| asa:31566704 |  | 49,156 | 352 | 4,714 |
| asa:849191641 |  | 45,317 | 2,626 | 8,825 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,552 | 28 | 36 |
| asa:3203964481 |  | 2,566 | 23 | 76 |
| asa:1134696561 |  | 2,082 | 28 | 41 |
| asa:452399768 |  | 1,160 | 65 | 77 |
| asa:2699078351 |  | 732 | 201 | 203 |
| asa:3178895177 |  | 586 | 50 | 100 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.6 / 3.1 ms; 3,002 req/s, p95 10 ms | 0.5 / 0.6 / 0.7 ms; 4,673 req/s, p95 5 ms |
| account holdings | 2.0 / 4.7 / 8.0 ms; 1,727 req/s, p95 16 ms | 1.5 / 1.9 / 2.9 ms; 2,241 req/s, p95 10 ms |
| balance, latest | 2.9 / 6.3 / 8.4 ms; 1,415 req/s, p95 19 ms | 2.3 / 2.7 / 3.2 ms; 1,586 req/s, p95 15 ms |
| balance, past round | 2.4 / 2.9 / 3.5 ms; 1,907 req/s, p95 13 ms | 1.7 / 2.1 / 2.4 ms; 1,882 req/s, p95 12 ms |
| history, busiest account | 4.2 / 9.0 / 15.9 ms; 664 req/s, p95 43 ms | 4.5 / 5.9 / 6.7 ms; 692 req/s, p95 39 ms |
| history, typical account | 1.7 / 1.9 / 2.6 ms; 1,709 req/s, p95 17 ms | 1.4 / 2.0 / 2.4 ms; 2,293 req/s, p95 11 ms |
| holders, native, latest | 15.2 / 16.7 / 18.2 ms; 270 req/s, p95 70 ms | 2.1 / 2.8 / 3.2 ms; 1,224 req/s, p95 19 ms |
| holders, native, past round | 31.7 / 39.0 / 44.0 ms; 68 req/s, p95 284 ms | 7.1 / 7.8 / 8.0 ms; 314 req/s, p95 76 ms |
| holders, busiest ASA, latest | 54.8 / 57.3 / 61.3 ms; 85 req/s, p95 235 ms | 2.1 / 2.5 / 2.7 ms; 1,082 req/s, p95 20 ms |
| token info | 0.7 / 0.9 / 1.0 ms; 6,420 req/s, p95 4 ms | 0.4 / 0.5 / 0.6 ms; 6,929 req/s, p95 4 ms |

## voi

Measured on 2026-11-07 at commit `6396135a`: the 100,000 newest rounds of voi (chain rounds 24,203,651 to 24,303,650) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 2,691 rounds/s | 7,622 rounds/s |
| Effects written | 75,705 (2,037/s) | 75,705 (5,770/s) |
| Time for the range | 37.2 s | 13.1 s |
| Stored size | 8.9 MiB | 49.9 MiB |
| Per round | 0.09 KiB | 0.51 KiB |
| Per effect | 123 bytes | 689 bytes |

Block download is the same for both and not part of the table: 21 rounds/s from the public archival nodes (18,805 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 5.0 MiB, effects_by_token_bucket 3.6 MiB, effect_buckets_by_account_token 0.1 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 29.5 MiB, effects_by_token_bucket 19.5 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 75 ARC-200 tokens, 14 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 53,310 | 154 | 212 |
| arc200:390001 |  | 7,660 | 25 | 37 |
| arc200:40153155 |  | 1,798 | 9 | 23 |
| arc200:395614 |  | 1,396 | 9 | 18 |
| arc200:47138068 |  | 1,283 | 10 | 19 |
| arc200:410111 |  | 1,240 | 19 | 26 |
| arc200:420024 |  | 1,060 | 10 | 13 |
| arc200:300279 |  | 968 | 30 | 34 |
| arc200:420069 |  | 812 | 15 | 27 |
| asa:40152679 |  | 801 | 5 | 9 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.4 / 2.4 ms; 3,071 req/s, p95 10 ms | 0.6 / 0.8 / 2.4 ms; 4,434 req/s, p95 5 ms |
| account holdings | 1.7 / 2.5 / 3.5 ms; 1,608 req/s, p95 18 ms | 1.5 / 2.0 / 2.8 ms; 2,218 req/s, p95 11 ms |
| balance, latest | 2.7 / 3.0 / 3.9 ms; 1,602 req/s, p95 16 ms | 2.2 / 2.6 / 3.2 ms; 1,491 req/s, p95 16 ms |
| balance, past round | 2.5 / 2.9 / 3.6 ms; 1,678 req/s, p95 14 ms | 1.9 / 2.4 / 3.0 ms; 1,613 req/s, p95 14 ms |
| history, busiest account | 6.7 / 8.8 / 9.9 ms; 730 req/s, p95 38 ms | 5.0 / 7.3 / 8.2 ms; 630 req/s, p95 40 ms |
| history, typical account | 1.7 / 3.1 / 3.9 ms; 2,184 req/s, p95 13 ms | 1.5 / 2.7 / 3.5 ms; 1,855 req/s, p95 14 ms |
| holders, native, latest | 5.1 / 6.6 / 8.2 ms; 431 req/s, p95 57 ms | 2.5 / 3.2 / 3.8 ms; 1,062 req/s, p95 22 ms |
| holders, native, past round | 21.5 / 26.5 / 28.7 ms; 78 req/s, p95 240 ms | 8.8 / 9.6 / 10.4 ms; 266 req/s, p95 88 ms |
| holders, busiest ASA, latest | 2.2 / 2.7 / 3.1 ms; 1,521 req/s, p95 16 ms | 1.4 / 1.5 / 1.8 ms; 2,630 req/s, p95 8 ms |
| holders, busiest ARC-200, latest | 2.6 / 3.5 / 4.0 ms; 1,144 req/s, p95 21 ms | 1.5 / 2.0 / 2.5 ms; 2,022 req/s, p95 11 ms |
| token info | 0.7 / 0.8 / 0.8 ms; 6,295 req/s, p95 4 ms | 0.5 / 0.5 / 0.6 ms; 8,436 req/s, p95 3 ms |

