# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-03 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,587,710 to 66,597,709) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 106 rounds/s | 393 rounds/s |
| Effects written | 470,925 (5,002/s) | 470,925 (18,500/s) |
| Time for the range | 94.1 s | 25.5 s |
| Stored size | 83.0 MiB | 323.6 MiB |
| Per round | 8.50 KiB | 33.14 KiB |
| Per effect | 179 bytes | 697 bytes |
| If all 66 M MainNet rounds were like these | 0.5 TiB | 2.0 TiB |

Block download is the same for both and not part of the table: 318 rounds/s from the public archival nodes (138,388 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 42.1 MiB, effects_by_token_bucket 31.4 MiB, effect_buckets_by_account_token 3.7 MiB, holdings_by_token 1.8 MiB, accounts_touched_by_token 1.8 MiB
- PostgreSQL 17.11: effects_by_account_token 171.7 MiB, effects_by_token_bucket 127.4 MiB, holdings_by_token 7.7 MiB, accounts_touched_by_token 7.7 MiB, holdings_by_account 7.0 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 1 ARC-200 tokens, 5,270 ASAs. 2,173 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 321,864 | 4,247 | 13,646 |
| asa:31566704 |  | 66,090 | 424 | 3,607 |
| asa:849191641 |  | 43,453 | 2,606 | 8,791 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,542 | 13 | 20 |
| asa:2543151056 |  | 1,562 | 398 | 399 |
| asa:2726252423 |  | 1,107 | 25 | 47 |
| asa:452399768 |  | 941 | 57 | 68 |
| asa:2004387843 |  | 730 | 70 | 80 |
| asa:246519683 |  | 609 | 31 | 56 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.1 / 2.9 / 7.6 ms; 3,129 req/s, p95 9 ms | 0.4 / 0.6 / 0.8 ms; 5,994 req/s, p95 5 ms |
| account holdings | 2.4 / 4.2 / 5.4 ms; 1,336 req/s, p95 22 ms | 1.3 / 2.3 / 2.9 ms; 2,575 req/s, p95 10 ms |
| balance, latest | 3.0 / 4.5 / 7.1 ms; 1,275 req/s, p95 21 ms | 2.2 / 2.8 / 3.4 ms; 1,688 req/s, p95 16 ms |
| balance, past round | 2.6 / 3.0 / 3.5 ms; 1,709 req/s, p95 14 ms | 1.7 / 2.3 / 3.0 ms; 1,866 req/s, p95 13 ms |
| history, busiest account | 4.9 / 11.0 / 13.0 ms; 663 req/s, p95 45 ms | 3.8 / 5.3 / 7.6 ms; 756 req/s, p95 34 ms |
| history, typical account | 1.8 / 2.1 / 2.4 ms; 2,360 req/s, p95 12 ms | 1.4 / 1.8 / 2.3 ms; 2,488 req/s, p95 12 ms |
| holders, native, latest | 14.6 / 16.7 / 19.0 ms; 244 req/s, p95 88 ms | 2.0 / 3.2 / 4.3 ms; 1,264 req/s, p95 20 ms |
| holders, native, past round | 29.1 / 32.6 / 35.6 ms; 66 req/s, p95 292 ms | 7.1 / 7.8 / 12.3 ms; 347 req/s, p95 70 ms |
| holders, busiest ASA, latest | 44.5 / 48.0 / 57.1 ms; 110 req/s, p95 171 ms | 1.9 / 2.4 / 2.5 ms; 1,286 req/s, p95 17 ms |
| holders, busiest ARC-200, latest | 2.1 / 3.0 / 3.9 ms; 1,290 req/s, p95 22 ms | 1.0 / 1.8 / 2.2 ms; 3,775 req/s, p95 7 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 6,239 req/s, p95 4 ms | 0.4 / 0.5 / 0.6 ms; 9,794 req/s, p95 3 ms |

## voi

Measured on 2026-11-03 at commit `9e2e5bda`: the 100,000 newest rounds of voi (chain rounds 24,079,854 to 24,179,853) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 4,725 rounds/s | 14,022 rounds/s |
| Effects written | 19,446 (919/s) | 19,446 (2,727/s) |
| Time for the range | 21.2 s | 7.1 s |
| Stored size | 1.8 MiB | 13.9 MiB |
| Per round | 0.02 KiB | 0.14 KiB |
| Per effect | 97 bytes | 747 bytes |

Block download is the same for both and not part of the table: 28 rounds/s from the public archival nodes (5,437 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.0 MiB, effects_by_token_bucket 0.7 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, holdings_by_account 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 8.6 MiB, effects_by_token_bucket 4.8 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 15 ARC-200 tokens, 3 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 17,962 | 41 | 69 |
| asa:586896 |  | 590 | 1 | 2 |
| arc200:390001 |  | 212 | 8 | 10 |
| arc200:420069 |  | 134 | 8 | 12 |
| arc200:395614 |  | 120 | 2 | 11 |
| arc200:300279 |  | 88 | 14 | 17 |
| arc200:404004 |  | 60 | 13 | 14 |
| arc200:411311 |  | 60 | 13 | 14 |
| arc200:416000 |  | 60 | 13 | 14 |
| arc200:420019 |  | 60 | 13 | 14 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.5 / 2.8 ms; 3,035 req/s, p95 10 ms | 0.5 / 0.6 / 0.6 ms; 6,992 req/s, p95 3 ms |
| account holdings | 1.8 / 3.1 / 4.4 ms; 1,873 req/s, p95 15 ms | 1.1 / 1.4 / 2.1 ms; 3,124 req/s, p95 9 ms |
| balance, latest | 2.9 / 3.7 / 4.9 ms; 1,515 req/s, p95 17 ms | 1.7 / 2.2 / 3.0 ms; 2,138 req/s, p95 12 ms |
| balance, past round | 2.7 / 3.3 / 5.1 ms; 1,599 req/s, p95 16 ms | 1.5 / 2.1 / 2.6 ms; 2,251 req/s, p95 10 ms |
| history, busiest account | 6.1 / 8.9 / 9.2 ms; 654 req/s, p95 49 ms | 3.7 / 5.7 / 8.0 ms; 996 req/s, p95 26 ms |
| history, typical account | 1.9 / 3.5 / 5.8 ms; 1,693 req/s, p95 17 ms | 1.3 / 2.5 / 3.2 ms; 2,132 req/s, p95 12 ms |
| holders, native, latest | 3.0 / 4.3 / 5.0 ms; 897 req/s, p95 30 ms | 1.2 / 1.8 / 3.2 ms; 2,729 req/s, p95 9 ms |
| holders, native, past round | 8.3 / 12.0 / 13.9 ms; 204 req/s, p95 109 ms | 4.9 / 5.4 / 5.9 ms; 459 req/s, p95 54 ms |
| holders, busiest ASA, latest | 2.1 / 2.7 / 3.0 ms; 1,562 req/s, p95 17 ms | 0.8 / 0.9 / 1.1 ms; 4,741 req/s, p95 5 ms |
| holders, busiest ARC-200, latest | 2.3 / 3.1 / 4.0 ms; 1,342 req/s, p95 19 ms | 0.8 / 1.0 / 1.9 ms; 4,486 req/s, p95 5 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 6,610 req/s, p95 4 ms | 0.2 / 0.2 / 0.3 ms; 15,604 req/s, p95 2 ms |

