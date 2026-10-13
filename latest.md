# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-13 at commit `bd39dd2f`: the 10,000 newest rounds of algorand-mainnet (chain rounds 65,931,125 to 65,941,124) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 82 rounds/s | 166 rounds/s |
| Effects written | 844,940 (6,911/s) | 844,940 (13,984/s) |
| Time for the range | 122.3 s | 60.4 s |
| Stored size | 134.8 MiB | 559.4 MiB |
| Per round | 13.80 KiB | 57.28 KiB |
| Per effect | 165 bytes | 683 bytes |
| If all 66 M MainNet rounds were like these | 0.8 TiB | 3.5 TiB |

Block download is the same for both and not part of the table: 215 rounds/s from the public archival nodes (230,822 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 68.0 MiB, effects_by_token_bucket 55.0 MiB, effect_buckets_by_account_token 4.4 MiB, holdings_by_token 2.2 MiB, accounts_touched_by_token 2.2 MiB
- PostgreSQL 17.11: effects_by_account_token 302.4 MiB, effects_by_token_bucket 225.9 MiB, holdings_by_token 9.7 MiB, accounts_touched_by_token 9.6 MiB, holdings_by_account 8.8 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 3 ARC-200 tokens, 8,109 ASAs. 5,286 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 585,484 | 6,618 | 15,013 |
| asa:127746157 |  | 53,906 | 0 | 1 |
| asa:849191641 |  | 49,128 | 2,330 | 9,180 |
| asa:31566704 |  | 38,090 | 480 | 4,250 |
| asa:127745593 |  | 29,903 | 0 | 1 |
| asa:127746786 |  | 19,731 | 0 | 1 |
| asa:3145862805 |  | 12,960 | 1,801 | 1,802 |
| asa:1284444444 |  | 7,411 | 26 | 64 |
| asa:3737581431 |  | 4,800 | 23 | 24 |
| asa:1294765516 |  | 2,667 | 1 | 3 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.1 / 2.4 / 4.2 ms; 4,035 req/s, p95 7 ms | 0.5 / 0.5 / 0.6 ms; 6,003 req/s, p95 4 ms |
| account holdings | 2.1 / 3.6 / 5.0 ms; 1,482 req/s, p95 20 ms | 1.4 / 1.8 / 2.6 ms; 2,254 req/s, p95 10 ms |
| balance, latest | 3.2 / 4.9 / 6.4 ms; 1,177 req/s, p95 28 ms | 2.3 / 2.8 / 3.3 ms; 1,533 req/s, p95 19 ms |
| balance, past round | 2.5 / 3.0 / 3.5 ms; 2,372 req/s, p95 11 ms | 1.6 / 2.2 / 2.4 ms; 1,919 req/s, p95 13 ms |
| history, busiest account | 3.9 / 6.4 / 9.1 ms; 691 req/s, p95 44 ms | 3.1 / 4.6 / 6.1 ms; 891 req/s, p95 27 ms |
| history, typical account | 1.8 / 2.1 / 2.4 ms; 3,086 req/s, p95 8 ms | 1.5 / 2.0 / 2.4 ms; 2,204 req/s, p95 13 ms |
| holders, native, latest | 9.9 / 12.6 / 14.4 ms; 309 req/s, p95 90 ms | 2.2 / 3.1 / 3.9 ms; 1,171 req/s, p95 21 ms |
| holders, native, past round | 29.7 / 35.3 / 40.5 ms; 68 req/s, p95 284 ms | 8.8 / 9.6 / 10.0 ms; 297 req/s, p95 79 ms |
| holders, busiest ASA, latest | 2.0 / 2.8 / 3.7 ms; 1,754 req/s, p95 14 ms | 1.2 / 1.8 / 3.4 ms; 2,981 req/s, p95 8 ms |
| holders, busiest ARC-200, latest | 2.1 / 2.5 / 3.0 ms; 1,824 req/s, p95 14 ms | 1.2 / 1.3 / 1.7 ms; 2,994 req/s, p95 8 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 7,878 req/s, p95 3 ms | 0.5 / 0.5 / 0.6 ms; 7,078 req/s, p95 4 ms |

## voi

Measured on 2026-10-13 at commit `bd39dd2f`: the 100,000 newest rounds of voi (chain rounds 23,428,688 to 23,528,687) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 5,068 rounds/s | 5,045 rounds/s |
| Effects written | 27,580 (1,398/s) | 27,580 (1,391/s) |
| Time for the range | 19.7 s | 19.8 s |
| Stored size | 3.4 MiB | 20.1 MiB |
| Per round | 0.04 KiB | 0.21 KiB |
| Per effect | 130 bytes | 759 bytes |

Block download is the same for both and not part of the table: 114 rounds/s from the public archival nodes (10,188 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 2.0 MiB, effects_by_token_bucket 1.4 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 12.1 MiB, effects_by_token_bucket 7.4 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 28 ARC-200 tokens, 6 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 23,834 | 45 | 74 |
| asa:586896 |  | 1,602 | 1 | 2 |
| arc200:390001 |  | 664 | 12 | 26 |
| arc200:420069 |  | 280 | 20 | 26 |
| arc200:47138068 |  | 189 | 5 | 20 |
| arc200:395614 |  | 172 | 9 | 14 |
| arc200:300279 |  | 96 | 20 | 24 |
| arc200:404004 |  | 84 | 17 | 21 |
| asa:302190 |  | 74 | 4 | 10 |
| arc200:411311 |  | 72 | 16 | 17 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 1.1 / 2.5 ms; 3,512 req/s, p95 8 ms | 0.5 / 0.6 / 0.6 ms; 5,778 req/s, p95 4 ms |
| account holdings | 1.8 / 2.0 / 2.9 ms; 1,772 req/s, p95 17 ms | 1.4 / 1.9 / 2.6 ms; 2,240 req/s, p95 11 ms |
| balance, latest | 2.9 / 3.3 / 3.6 ms; 1,345 req/s, p95 20 ms | 2.2 / 2.7 / 2.9 ms; 1,705 req/s, p95 15 ms |
| balance, past round | 2.8 / 3.3 / 4.8 ms; 1,751 req/s, p95 13 ms | 1.8 / 2.3 / 3.0 ms; 1,949 req/s, p95 13 ms |
| history, busiest account | 6.2 / 10.6 / 13.8 ms; 762 req/s, p95 38 ms | 4.6 / 6.3 / 6.9 ms; 926 req/s, p95 28 ms |
| history, typical account | 1.9 / 3.3 / 4.0 ms; 2,065 req/s, p95 13 ms | 1.5 / 2.8 / 3.9 ms; 1,790 req/s, p95 15 ms |
| holders, native, latest | 3.1 / 4.2 / 5.6 ms; 890 req/s, p95 30 ms | 1.8 / 2.5 / 3.0 ms; 1,740 req/s, p95 14 ms |
| holders, native, past round | 8.8 / 11.7 / 14.8 ms; 189 req/s, p95 114 ms | 6.2 / 7.1 / 9.9 ms; 351 req/s, p95 69 ms |
| holders, busiest ASA, latest | 1.9 / 2.3 / 2.6 ms; 1,577 req/s, p95 17 ms | 1.1 / 1.2 / 1.3 ms; 3,124 req/s, p95 8 ms |
| holders, busiest ARC-200, latest | 2.3 / 2.9 / 3.6 ms; 1,278 req/s, p95 20 ms | 1.3 / 1.4 / 1.9 ms; 2,608 req/s, p95 9 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 8,000 req/s, p95 3 ms | 0.4 / 0.4 / 0.5 ms; 8,403 req/s, p95 4 ms |

