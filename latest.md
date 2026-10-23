# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-23 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,241,230 to 66,251,229) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 143 rounds/s | 262 rounds/s |
| Effects written | 602,373 (8,619/s) | 602,373 (15,809/s) |
| Time for the range | 69.9 s | 38.1 s |
| Stored size | 98.0 MiB | 405.0 MiB |
| Per round | 10.04 KiB | 41.47 KiB |
| Per effect | 168 bytes | 694 bytes |
| If all 66 M MainNet rounds were like these | 0.6 TiB | 2.5 TiB |

Block download is the same for both and not part of the table: 219 rounds/s from the public archival nodes (177,158 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 49.9 MiB, effects_by_token_bucket 37.7 MiB, effect_buckets_by_account_token 3.8 MiB, holdings_by_token 2.0 MiB, accounts_touched_by_token 2.0 MiB
- PostgreSQL 17.11: effects_by_account_token 216.8 MiB, effects_by_token_bucket 160.5 MiB, holdings_by_token 8.0 MiB, accounts_touched_by_token 8.0 MiB, holdings_by_account 7.5 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 7 ARC-200 tokens, 9,168 ASAs. 6,694 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 465,980 | 4,706 | 13,004 |
| asa:31566704 |  | 65,306 | 478 | 4,357 |
| asa:849191641 |  | 12,343 | 712 | 3,590 |
| asa:3145862805 |  | 12,028 | 1,692 | 1,693 |
| asa:1284444444 |  | 5,341 | 12 | 27 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:2726252423 |  | 1,166 | 41 | 76 |
| asa:329110405 |  | 1,156 | 71 | 84 |
| asa:452399768 |  | 1,156 | 60 | 76 |
| asa:1294765516 |  | 938 | 1 | 2 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.2 / 1.9 / 3.5 ms; 4,393 req/s, p95 7 ms | 0.7 / 1.0 / 1.5 ms; 3,569 req/s, p95 8 ms |
| account holdings | 2.1 / 3.5 / 4.4 ms; 1,892 req/s, p95 15 ms | 1.9 / 2.3 / 3.1 ms; 1,629 req/s, p95 15 ms |
| balance, latest | 3.1 / 5.2 / 8.8 ms; 1,510 req/s, p95 20 ms | 2.8 / 3.3 / 4.0 ms; 1,137 req/s, p95 22 ms |
| balance, past round | 2.4 / 2.9 / 3.8 ms; 2,022 req/s, p95 13 ms | 2.2 / 2.8 / 3.1 ms; 1,319 req/s, p95 19 ms |
| history, busiest account | 3.9 / 9.8 / 14.9 ms; 840 req/s, p95 34 ms | 5.2 / 7.8 / 8.7 ms; 794 req/s, p95 30 ms |
| history, typical account | 1.4 / 1.8 / 2.8 ms; 2,508 req/s, p95 14 ms | 1.9 / 2.1 / 2.5 ms; 1,539 req/s, p95 17 ms |
| holders, native, latest | 9.3 / 11.1 / 13.2 ms; 405 req/s, p95 51 ms | 2.8 / 3.5 / 4.7 ms; 934 req/s, p95 24 ms |
| holders, native, past round | 30.2 / 35.7 / 39.3 ms; 86 req/s, p95 228 ms | 9.1 / 10.7 / 12.6 ms; 235 req/s, p95 99 ms |
| holders, busiest ASA, latest | 32.4 / 34.2 / 36.4 ms; 156 req/s, p95 125 ms | 3.0 / 3.6 / 4.0 ms; 865 req/s, p95 25 ms |
| holders, busiest ARC-200, latest | 1.6 / 2.0 / 2.4 ms; 2,342 req/s, p95 12 ms | 1.6 / 1.8 / 2.7 ms; 1,981 req/s, p95 12 ms |
| token info | 0.5 / 0.6 / 0.7 ms; 10,650 req/s, p95 3 ms | 0.6 / 1.0 / 1.6 ms; 5,018 req/s, p95 6 ms |

