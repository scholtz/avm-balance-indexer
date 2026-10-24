# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-24 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,272,265 to 66,282,264) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 73 rounds/s | 203 rounds/s |
| Effects written | 974,509 (7,114/s) | 974,509 (19,828/s) |
| Time for the range | 137.0 s | 49.1 s |
| Stored size | 155.6 MiB | 646.8 MiB |
| Per round | 15.93 KiB | 66.23 KiB |
| Per effect | 165 bytes | 687 bytes |
| If all 66 M MainNet rounds were like these | 1.0 TiB | 4.1 TiB |

Block download is the same for both and not part of the table: 258 rounds/s from the public archival nodes (281,247 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 78.4 MiB, effects_by_token_bucket 62.2 MiB, effect_buckets_by_account_token 5.4 MiB, holdings_by_token 2.8 MiB, accounts_touched_by_token 2.8 MiB
- PostgreSQL 17.11: effects_by_account_token 347.5 MiB, effects_by_token_bucket 259.7 MiB, holdings_by_token 11.7 MiB, accounts_touched_by_token 11.6 MiB, holdings_by_account 10.7 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 2 ARC-200 tokens, 13,803 ASAs. 7,801 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 699,648 | 6,716 | 19,728 |
| asa:31566704 |  | 79,340 | 361 | 8,031 |
| asa:127746157 |  | 51,698 | 0 | 1 |
| asa:127745593 |  | 32,370 | 0 | 1 |
| asa:849191641 |  | 27,325 | 1,713 | 5,554 |
| asa:127746786 |  | 19,102 | 0 | 1 |
| asa:3145862805 |  | 12,061 | 1,634 | 1,635 |
| asa:1284444444 |  | 5,536 | 33 | 43 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:2726252423 |  | 1,315 | 29 | 57 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 2.0 / 3.3 ms; 3,337 req/s, p95 9 ms | 0.7 / 1.0 / 3.7 ms; 3,700 req/s, p95 6 ms |
| account holdings | 2.1 / 5.3 / 17.4 ms; 1,642 req/s, p95 17 ms | 1.8 / 2.7 / 3.5 ms; 1,739 req/s, p95 14 ms |
| balance, latest | 2.9 / 3.9 / 5.0 ms; 1,365 req/s, p95 18 ms | 2.7 / 3.4 / 4.3 ms; 1,180 req/s, p95 22 ms |
| balance, past round | 2.6 / 3.6 / 5.1 ms; 1,523 req/s, p95 16 ms | 2.2 / 3.2 / 5.4 ms; 1,360 req/s, p95 18 ms |
| history, busiest account | 3.9 / 12.6 / 24.4 ms; 675 req/s, p95 44 ms | 5.3 / 8.0 / 9.8 ms; 843 req/s, p95 28 ms |
| history, typical account | 1.8 / 2.1 / 2.5 ms; 2,753 req/s, p95 9 ms | 1.8 / 2.1 / 2.9 ms; 1,661 req/s, p95 16 ms |
| holders, native, latest | 13.5 / 14.7 / 16.3 ms; 275 req/s, p95 77 ms | 2.7 / 3.5 / 4.3 ms; 948 req/s, p95 24 ms |
| holders, native, past round | 39.1 / 43.6 / 45.5 ms; 51 req/s, p95 383 ms | 8.5 / 9.0 / 9.5 ms; 244 req/s, p95 93 ms |
| holders, busiest ASA, latest | 111.7 / 122.2 / 126.0 ms; 47 req/s, p95 390 ms | 3.1 / 3.6 / 3.7 ms; 716 req/s, p95 31 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.4 / 2.9 ms; 1,768 req/s, p95 15 ms | 1.6 / 1.7 / 2.6 ms; 2,110 req/s, p95 12 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 5,743 req/s, p95 6 ms | 0.6 / 0.7 / 0.7 ms; 5,065 req/s, p95 6 ms |

