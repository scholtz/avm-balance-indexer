# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-14 at commit `592eed93`: the 10,000 newest rounds of algorand-mainnet (chain rounds 65,959,965 to 65,969,964) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 98 rounds/s | 190 rounds/s |
| Effects written | 1,000,351 (9,808/s) | 1,000,351 (19,049/s) |
| Time for the range | 102.0 s | 52.5 s |
| Stored size | 159.3 MiB | 658.9 MiB |
| Per round | 16.31 KiB | 67.48 KiB |
| Per effect | 165 bytes | 681 bytes |
| If all 66 M MainNet rounds were like these | 1.0 TiB | 4.1 TiB |

Block download is the same for both and not part of the table: 223 rounds/s from the public archival nodes (291,131 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 81.3 MiB, effects_by_token_bucket 64.6 MiB, effect_buckets_by_account_token 5.0 MiB, holdings_by_token 2.5 MiB, accounts_touched_by_token 2.5 MiB
- PostgreSQL 17.11: effects_by_account_token 356.6 MiB, effects_by_token_bucket 267.2 MiB, holdings_by_token 11.0 MiB, accounts_touched_by_token 10.9 MiB, holdings_by_account 10.0 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 8,054 ASAs. 5,323 of those ASAs were created inside the range, 1 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 706,660 | 6,948 | 18,164 |
| asa:31566704 |  | 85,579 | 542 | 5,636 |
| asa:127746157 |  | 54,917 | 0 | 1 |
| asa:849191641 |  | 35,263 | 1,893 | 7,374 |
| asa:127745593 |  | 29,280 | 0 | 1 |
| asa:127746786 |  | 19,382 | 0 | 1 |
| asa:3145862805 |  | 11,774 | 1,577 | 1,578 |
| asa:1284444444 |  | 5,723 | 24 | 44 |
| asa:3737581431 |  | 4,798 | 24 | 24 |
| asa:796425061 |  | 2,074 | 75 | 146 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.7 / 3.0 ms; 5,772 req/s, p95 5 ms | 0.6 / 0.7 / 0.8 ms; 5,814 req/s, p95 4 ms |
| account holdings | 1.9 / 3.6 / 5.4 ms; 2,072 req/s, p95 14 ms | 1.6 / 2.5 / 3.3 ms; 2,037 req/s, p95 11 ms |
| balance, latest | 2.7 / 4.1 / 6.4 ms; 1,732 req/s, p95 16 ms | 2.4 / 3.2 / 4.0 ms; 1,472 req/s, p95 17 ms |
| balance, past round | 2.4 / 3.6 / 4.8 ms; 1,990 req/s, p95 17 ms | 2.0 / 2.5 / 3.4 ms; 1,594 req/s, p95 15 ms |
| history, busiest account | 3.8 / 7.6 / 11.8 ms; 707 req/s, p95 44 ms | 3.5 / 5.2 / 6.9 ms; 870 req/s, p95 28 ms |
| history, typical account | 1.5 / 1.7 / 2.3 ms; 3,585 req/s, p95 7 ms | 1.7 / 2.2 / 2.8 ms; 1,910 req/s, p95 15 ms |
| holders, native, latest | 10.5 / 12.5 / 14.2 ms; 369 req/s, p95 56 ms | 2.4 / 3.0 / 3.7 ms; 1,090 req/s, p95 23 ms |
| holders, native, past round | 32.1 / 36.1 / 42.7 ms; 73 req/s, p95 267 ms | 7.6 / 8.3 / 8.8 ms; 305 req/s, p95 78 ms |
| holders, busiest ASA, latest | 34.5 / 37.2 / 42.4 ms; 141 req/s, p95 134 ms | 2.7 / 3.2 / 3.4 ms; 958 req/s, p95 24 ms |
| token info | 0.5 / 0.7 / 0.7 ms; 14,119 req/s, p95 2 ms | 0.5 / 0.6 / 0.6 ms; 7,485 req/s, p95 4 ms |

## voi

Measured on 2026-10-14 at commit `592eed93`: the 100,000 newest rounds of voi (chain rounds 23,457,187 to 23,557,186) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 4,489 rounds/s | 7,912 rounds/s |
| Effects written | 59,878 (2,688/s) | 59,878 (4,737/s) |
| Time for the range | 22.3 s | 12.6 s |
| Stored size | 6.9 MiB | 38.0 MiB |
| Per round | 0.07 KiB | 0.39 KiB |
| Per effect | 121 bytes | 663 bytes |

Block download is the same for both and not part of the table: 100 rounds/s from the public archival nodes (19,839 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 4.0 MiB, effects_by_token_bucket 2.8 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 21.4 MiB, effects_by_token_bucket 16.0 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 23 ARC-200 tokens, 6 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 56,444 | 43 | 68 |
| asa:586896 |  | 2,300 | 1 | 2 |
| arc200:390001 |  | 276 | 12 | 18 |
| arc200:47138068 |  | 122 | 4 | 19 |
| arc200:395614 |  | 92 | 7 | 10 |
| arc200:300279 |  | 90 | 20 | 24 |
| arc200:404004 |  | 76 | 17 | 18 |
| arc200:411311 |  | 76 | 17 | 18 |
| arc200:416000 |  | 76 | 17 | 18 |
| arc200:420019 |  | 76 | 17 | 18 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.2 / 3.7 ms; 4,791 req/s, p95 6 ms | 0.6 / 0.8 / 0.8 ms; 5,419 req/s, p95 4 ms |
| account holdings | 1.8 / 3.1 / 5.0 ms; 2,192 req/s, p95 14 ms | 1.7 / 2.2 / 3.4 ms; 2,007 req/s, p95 12 ms |
| balance, latest | 2.7 / 3.5 / 4.4 ms; 2,239 req/s, p95 11 ms | 2.4 / 2.8 / 3.6 ms; 1,533 req/s, p95 17 ms |
| balance, past round | 2.5 / 2.9 / 3.5 ms; 1,995 req/s, p95 13 ms | 1.9 / 2.5 / 3.0 ms; 1,801 req/s, p95 13 ms |
| history, busiest account | 6.0 / 9.2 / 11.3 ms; 789 req/s, p95 40 ms | 3.8 / 5.9 / 6.6 ms; 891 req/s, p95 27 ms |
| history, typical account | 1.6 / 2.7 / 3.6 ms; 2,667 req/s, p95 11 ms | 1.6 / 3.1 / 4.0 ms; 1,822 req/s, p95 14 ms |
| holders, native, latest | 2.5 / 3.8 / 4.7 ms; 1,105 req/s, p95 25 ms | 2.0 / 2.7 / 3.3 ms; 1,694 req/s, p95 14 ms |
| holders, native, past round | 5.9 / 8.3 / 9.6 ms; 267 req/s, p95 88 ms | 6.2 / 7.2 / 8.1 ms; 374 req/s, p95 65 ms |
| holders, busiest ASA, latest | 1.6 / 1.9 / 2.7 ms; 2,533 req/s, p95 10 ms | 1.3 / 1.4 / 1.7 ms; 3,364 req/s, p95 7 ms |
| holders, busiest ARC-200, latest | 1.8 / 2.3 / 2.9 ms; 1,721 req/s, p95 17 ms | 1.4 / 1.6 / 2.1 ms; 2,497 req/s, p95 9 ms |
| token info | 0.5 / 0.6 / 0.7 ms; 11,854 req/s, p95 2 ms | 0.4 / 0.5 / 0.5 ms; 9,855 req/s, p95 3 ms |

