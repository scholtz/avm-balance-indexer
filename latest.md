# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-21 at commit `ee61ce36`: the 10,000 newest rounds of algorand-mainnet (chain rounds 67,150,877 to 67,160,876) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 106 rounds/s | 184 rounds/s |
| Effects written | 889,979 (9,419/s) | 889,979 (16,375/s) |
| Time for the range | 94.5 s | 54.3 s |
| Stored size | 153.7 MiB | 604.9 MiB |
| Per round | 15.74 KiB | 61.95 KiB |
| Per effect | 177 bytes | 698 bytes |
| If all 66 M MainNet rounds were like these | 1.0 TiB | 3.8 TiB |

Block download is the same for both and not part of the table: 311 rounds/s from the public archival nodes (229,775 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 77.0 MiB, effects_by_token_bucket 58.9 MiB, effect_buckets_by_account_token 6.5 MiB, holdings_by_token 3.3 MiB, accounts_touched_by_token 3.3 MiB
- PostgreSQL 17.12: effects_by_account_token 319.0 MiB, effects_by_token_bucket 238.8 MiB, holdings_by_token 14.1 MiB, accounts_touched_by_token 14.0 MiB, holdings_by_account 12.6 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 16,264 ASAs. 9,155 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 594,714 | 7,642 | 20,231 |
| asa:31566704 |  | 65,292 | 451 | 9,993 |
| asa:849191641 |  | 48,664 | 3,017 | 8,750 |
| asa:127746157 |  | 34,358 | 0 | 1 |
| asa:127745593 |  | 19,994 | 0 | 1 |
| asa:3145862805 |  | 15,914 | 2,176 | 2,177 |
| asa:127746786 |  | 11,490 | 0 | 1 |
| asa:3203964481 |  | 6,648 | 41 | 88 |
| asa:1134696561 |  | 5,799 | 27 | 64 |
| asa:3737581431 |  | 4,800 | 24 | 24 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.3 / 3.0 / 4.7 ms; 3,114 req/s, p95 11 ms | 0.8 / 0.9 / 1.1 ms; 3,170 req/s, p95 9 ms |
| account holdings | 2.0 / 3.6 / 4.0 ms; 1,738 req/s, p95 17 ms | 1.8 / 2.1 / 2.2 ms; 1,491 req/s, p95 18 ms |
| balance, latest | 3.0 / 4.5 / 9.0 ms; 1,532 req/s, p95 16 ms | 2.9 / 3.6 / 4.2 ms; 1,057 req/s, p95 23 ms |
| balance, past round | 2.6 / 3.0 / 3.7 ms; 1,463 req/s, p95 17 ms | 2.3 / 2.9 / 3.6 ms; 1,189 req/s, p95 20 ms |
| history, busiest account | 8.1 / 13.5 / 18.2 ms; 596 req/s, p95 59 ms | 6.8 / 10.3 / 11.8 ms; 562 req/s, p95 58 ms |
| history, typical account | 1.5 / 1.8 / 2.5 ms; 3,110 req/s, p95 9 ms | 1.8 / 2.4 / 2.9 ms; 1,726 req/s, p95 14 ms |
| holders, native, latest | 8.6 / 11.5 / 15.5 ms; 399 req/s, p95 54 ms | 2.7 / 3.6 / 5.3 ms; 938 req/s, p95 25 ms |
| holders, native, past round | 32.7 / 39.3 / 45.4 ms; 77 req/s, p95 246 ms | 8.6 / 10.0 / 11.9 ms; 249 req/s, p95 93 ms |
| holders, busiest ASA, latest | 68.0 / 78.0 / 84.1 ms; 75 req/s, p95 246 ms | 3.3 / 4.0 / 6.2 ms; 692 req/s, p95 33 ms |
| token info | 0.6 / 0.8 / 0.9 ms; 9,073 req/s, p95 3 ms | 0.6 / 0.8 / 0.9 ms; 4,628 req/s, p95 6 ms |

## voi

Measured on 2026-11-21 at commit `ee61ce36`: the 100,000 newest rounds of voi (chain rounds 24,641,132 to 24,741,131) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 1,994 rounds/s | 4,984 rounds/s |
| Effects written | 160,030 (3,192/s) | 160,030 (7,975/s) |
| Time for the range | 50.1 s | 20.1 s |
| Stored size | 17.2 MiB | 90.3 MiB |
| Per round | 0.18 KiB | 0.92 KiB |
| Per effect | 113 bytes | 591 bytes |

Block download is the same for both and not part of the table: 62 rounds/s from the public archival nodes (59,068 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 9.1 MiB, effects_by_token_bucket 8.0 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_account 0.0 MiB, holdings_by_token 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 49.3 MiB, effects_by_token_bucket 40.4 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 14 ARC-200 tokens, 2 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 159,417 | 136 | 190 |
| asa:586896 |  | 200 | 1 | 2 |
| arc200:300279 |  | 62 | 13 | 14 |
| arc200:404004 |  | 62 | 13 | 14 |
| arc200:411311 |  | 62 | 13 | 14 |
| arc200:416000 |  | 62 | 13 | 14 |
| arc200:420019 |  | 62 | 13 | 14 |
| arc200:390001 |  | 26 | 4 | 9 |
| arc200:395614 |  | 22 | 4 | 8 |
| asa:302190 |  | 20 | 3 | 9 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.2 / 1.7 / 2.3 ms; 3,821 req/s, p95 8 ms | 0.8 / 0.9 / 1.0 ms; 3,285 req/s, p95 8 ms |
| account holdings | 1.7 / 2.3 / 2.8 ms; 2,007 req/s, p95 15 ms | 1.7 / 2.1 / 2.8 ms; 1,648 req/s, p95 15 ms |
| balance, latest | 2.7 / 3.1 / 3.6 ms; 1,651 req/s, p95 15 ms | 2.7 / 3.5 / 3.9 ms; 1,168 req/s, p95 20 ms |
| balance, past round | 2.6 / 3.0 / 3.6 ms; 1,622 req/s, p95 16 ms | 2.3 / 3.0 / 3.5 ms; 1,228 req/s, p95 19 ms |
| history, busiest account | 8.1 / 10.4 / 11.8 ms; 567 req/s, p95 58 ms | 7.2 / 10.6 / 11.4 ms; 546 req/s, p95 60 ms |
| history, typical account | 1.6 / 3.0 / 4.2 ms; 2,701 req/s, p95 10 ms | 1.7 / 2.8 / 4.1 ms; 1,674 req/s, p95 14 ms |
| holders, native, latest | 4.5 / 5.9 / 9.9 ms; 631 req/s, p95 38 ms | 2.6 / 3.4 / 4.2 ms; 988 req/s, p95 23 ms |
| holders, native, past round | 12.9 / 16.5 / 19.4 ms; 117 req/s, p95 167 ms | 8.5 / 9.1 / 9.4 ms; 255 req/s, p95 92 ms |
| holders, busiest ASA, latest | 1.6 / 1.9 / 2.0 ms; 2,425 req/s, p95 10 ms | 1.5 / 1.8 / 2.2 ms; 1,708 req/s, p95 13 ms |
| holders, busiest ARC-200, latest | 1.9 / 2.3 / 3.1 ms; 1,763 req/s, p95 15 ms | 1.8 / 2.5 / 4.0 ms; 1,829 req/s, p95 12 ms |
| token info | 0.6 / 0.7 / 0.8 ms; 7,901 req/s, p95 4 ms | 0.6 / 0.6 / 0.7 ms; 4,882 req/s, p95 6 ms |

