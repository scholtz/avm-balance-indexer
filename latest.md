# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-26 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,335,514 to 66,345,513) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 87 rounds/s | 233 rounds/s |
| Effects written | 796,029 (6,908/s) | 796,029 (18,543/s) |
| Time for the range | 115.2 s | 42.9 s |
| Stored size | 125.6 MiB | 523.4 MiB |
| Per round | 12.86 KiB | 53.59 KiB |
| Per effect | 163 bytes | 678 bytes |
| If all 66 M MainNet rounds were like these | 0.8 TiB | 3.3 TiB |

Block download is the same for both and not part of the table: 201 rounds/s from the public archival nodes (230,483 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 63.5 MiB, effects_by_token_bucket 51.8 MiB, effect_buckets_by_account_token 3.9 MiB, holdings_by_token 1.9 MiB, accounts_touched_by_token 1.9 MiB
- PostgreSQL 17.11: effects_by_account_token 284.2 MiB, effects_by_token_bucket 212.4 MiB, holdings_by_token 8.3 MiB, accounts_touched_by_token 8.2 MiB, holdings_by_account 7.6 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 5 ARC-200 tokens, 6,723 ASAs. 3,017 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 579,482 | 4,796 | 14,896 |
| asa:127746157 |  | 53,655 | 0 | 1 |
| asa:31566704 |  | 32,286 | 343 | 4,426 |
| asa:849191641 |  | 31,191 | 1,961 | 6,615 |
| asa:127745593 |  | 29,673 | 0 | 1 |
| asa:127746786 |  | 20,545 | 0 | 1 |
| asa:3145862805 |  | 11,758 | 1,609 | 1,610 |
| asa:1284444444 |  | 5,484 | 16 | 50 |
| asa:3737581431 |  | 4,800 | 23 | 24 |
| asa:1294765516 |  | 1,005 | 2 | 4 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.1 / 1.9 / 2.9 ms; 3,077 req/s, p95 9 ms | 0.8 / 1.0 / 1.8 ms; 3,287 req/s, p95 7 ms |
| account holdings | 2.1 / 3.4 / 5.4 ms; 1,429 req/s, p95 20 ms | 1.9 / 3.0 / 4.6 ms; 1,565 req/s, p95 16 ms |
| balance, latest | 3.1 / 6.2 / 10.3 ms; 1,222 req/s, p95 22 ms | 3.0 / 3.8 / 4.3 ms; 1,068 req/s, p95 24 ms |
| balance, past round | 2.7 / 3.4 / 4.1 ms; 1,666 req/s, p95 16 ms | 2.5 / 3.5 / 4.4 ms; 1,321 req/s, p95 18 ms |
| history, busiest account | 4.5 / 10.3 / 15.0 ms; 700 req/s, p95 41 ms | 6.1 / 8.9 / 9.4 ms; 740 req/s, p95 32 ms |
| history, typical account | 1.8 / 2.1 / 2.7 ms; 2,791 req/s, p95 9 ms | 2.0 / 2.5 / 2.8 ms; 1,423 req/s, p95 19 ms |
| holders, native, latest | 13.2 / 14.8 / 16.5 ms; 273 req/s, p95 82 ms | 3.1 / 4.2 / 4.8 ms; 884 req/s, p95 25 ms |
| holders, native, past round | 38.8 / 45.6 / 50.4 ms; 58 req/s, p95 330 ms | 10.0 / 13.5 / 14.5 ms; 229 req/s, p95 103 ms |
| holders, busiest ASA, latest | 2.0 / 2.7 / 3.6 ms; 1,800 req/s, p95 14 ms | 1.7 / 2.0 / 3.3 ms; 2,103 req/s, p95 12 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.5 / 2.8 ms; 1,701 req/s, p95 14 ms | 1.7 / 2.1 / 4.1 ms; 1,861 req/s, p95 13 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 7,286 req/s, p95 4 ms | 0.6 / 0.8 / 1.2 ms; 4,361 req/s, p95 7 ms |

