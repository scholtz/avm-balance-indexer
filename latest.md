# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-08 at commit `6396135a`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,743,944 to 66,753,943) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 167 rounds/s | 462 rounds/s |
| Effects written | 410,241 (6,867/s) | 410,241 (18,971/s) |
| Time for the range | 59.7 s | 21.6 s |
| Stored size | 74.4 MiB | 286.5 MiB |
| Per round | 7.62 KiB | 29.33 KiB |
| Per effect | 183 bytes | 705 bytes |
| If all 66 M MainNet rounds were like these | 0.5 TiB | 1.8 TiB |

Block download is the same for both and not part of the table: 309 rounds/s from the public archival nodes (111,122 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 37.2 MiB, effects_by_token_bucket 27.8 MiB, effect_buckets_by_account_token 3.7 MiB, holdings_by_token 1.8 MiB, accounts_touched_by_token 1.7 MiB
- PostgreSQL 17.11: effects_by_account_token 149.4 MiB, effects_by_token_bucket 111.0 MiB, holdings_by_token 8.2 MiB, accounts_touched_by_token 8.2 MiB, holdings_by_account 7.5 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 2 ARC-200 tokens, 5,310 ASAs. 2,108 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 288,220 | 3,818 | 13,552 |
| asa:849191641 |  | 46,446 | 2,600 | 8,693 |
| asa:31566704 |  | 39,512 | 426 | 4,060 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,532 | 13 | 22 |
| asa:2543151056 |  | 1,574 | 401 | 402 |
| asa:2726252423 |  | 1,144 | 37 | 75 |
| asa:452399768 |  | 918 | 54 | 72 |
| asa:2699078351 |  | 739 | 200 | 204 |
| asa:246519683 |  | 688 | 43 | 56 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.1 / 1.9 / 3.9 ms; 4,174 req/s, p95 7 ms | 0.4 / 0.5 / 1.0 ms; 7,784 req/s, p95 3 ms |
| account holdings | 2.4 / 4.3 / 5.6 ms; 1,559 req/s, p95 19 ms | 1.2 / 1.9 / 2.6 ms; 2,456 req/s, p95 10 ms |
| balance, latest | 3.1 / 5.4 / 6.9 ms; 1,674 req/s, p95 16 ms | 2.0 / 2.6 / 3.4 ms; 1,903 req/s, p95 13 ms |
| balance, past round | 2.5 / 3.5 / 4.8 ms; 2,149 req/s, p95 12 ms | 1.4 / 1.9 / 2.7 ms; 2,118 req/s, p95 11 ms |
| history, busiest account | 5.9 / 10.2 / 13.2 ms; 819 req/s, p95 33 ms | 2.9 / 3.9 / 5.5 ms; 1,025 req/s, p95 25 ms |
| history, typical account | 1.5 / 1.6 / 2.3 ms; 4,154 req/s, p95 7 ms | 1.4 / 1.9 / 2.3 ms; 2,525 req/s, p95 10 ms |
| holders, native, latest | 11.2 / 12.8 / 14.3 ms; 336 req/s, p95 63 ms | 1.9 / 3.1 / 3.7 ms; 1,513 req/s, p95 16 ms |
| holders, native, past round | 22.5 / 25.4 / 27.7 ms; 89 req/s, p95 219 ms | 5.9 / 6.7 / 7.9 ms; 375 req/s, p95 61 ms |
| holders, busiest ASA, latest | 9.9 / 12.3 / 13.6 ms; 362 req/s, p95 60 ms | 1.6 / 2.0 / 2.1 ms; 1,619 req/s, p95 14 ms |
| holders, busiest ARC-200, latest | 1.7 / 2.2 / 2.9 ms; 2,078 req/s, p95 13 ms | 0.9 / 1.1 / 1.4 ms; 4,125 req/s, p95 5 ms |
| token info | 0.6 / 0.7 / 0.7 ms; 10,646 req/s, p95 3 ms | 0.4 / 0.4 / 0.5 ms; 10,888 req/s, p95 2 ms |

