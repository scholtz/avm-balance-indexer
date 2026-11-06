# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-06 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,681,546 to 66,691,545) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 275 rounds/s | 415 rounds/s |
| Effects written | 362,563 (9,954/s) | 362,563 (15,041/s) |
| Time for the range | 36.4 s | 24.1 s |
| Stored size | 64.5 MiB | 252.7 MiB |
| Per round | 6.61 KiB | 25.87 KiB |
| Per effect | 180 bytes | 705 bytes |
| If all 66 M MainNet rounds were like these | 0.4 TiB | 1.6 TiB |

Block download is the same for both and not part of the table: 310 rounds/s from the public archival nodes (99,334 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 32.1 MiB, effects_by_token_bucket 23.9 MiB, effect_buckets_by_account_token 3.2 MiB, holdings_by_token 1.6 MiB, accounts_touched_by_token 1.6 MiB
- PostgreSQL 17.11: effects_by_account_token 132.9 MiB, effects_by_token_bucket 97.6 MiB, holdings_by_token 6.8 MiB, accounts_touched_by_token 6.8 MiB, holdings_by_account 6.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 9 ARC-200 tokens, 5,729 ASAs. 3,801 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 271,600 | 3,807 | 12,638 |
| asa:31566704 |  | 33,910 | 317 | 3,889 |
| asa:849191641 |  | 19,090 | 1,125 | 4,674 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,506 | 15 | 23 |
| asa:2543151056 |  | 1,574 | 401 | 402 |
| asa:3160000000 |  | 1,454 | 39 | 81 |
| asa:452399768 |  | 1,260 | 55 | 69 |
| asa:246519683 |  | 1,061 | 46 | 60 |
| asa:3763665834 |  | 991 | 33 | 46 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.6 / 1.4 / 2.0 ms; 8,572 req/s, p95 4 ms | 0.7 / 0.9 / 1.2 ms; 4,720 req/s, p95 6 ms |
| account holdings | 1.3 / 3.6 / 8.6 ms; 2,725 req/s, p95 10 ms | 1.7 / 2.3 / 3.2 ms; 2,059 req/s, p95 12 ms |
| balance, latest | 1.7 / 4.4 / 6.0 ms; 2,296 req/s, p95 13 ms | 2.4 / 3.0 / 3.5 ms; 1,435 req/s, p95 17 ms |
| balance, past round | 1.5 / 2.1 / 2.9 ms; 2,942 req/s, p95 9 ms | 1.9 / 2.4 / 3.0 ms; 1,629 req/s, p95 14 ms |
| history, busiest account | 2.9 / 6.2 / 10.0 ms; 897 req/s, p95 34 ms | 3.0 / 4.1 / 6.5 ms; 879 req/s, p95 27 ms |
| history, typical account | 0.9 / 1.3 / 3.0 ms; 5,138 req/s, p95 6 ms | 1.6 / 2.0 / 2.5 ms; 2,064 req/s, p95 13 ms |
| holders, native, latest | 8.0 / 11.1 / 20.5 ms; 482 req/s, p95 41 ms | 2.3 / 3.2 / 3.8 ms; 1,161 req/s, p95 20 ms |
| holders, native, past round | 10.1 / 12.6 / 21.3 ms; 162 req/s, p95 126 ms | 7.3 / 8.1 / 9.9 ms; 324 req/s, p95 75 ms |
| holders, busiest ASA, latest | 21.4 / 23.9 / 29.3 ms; 206 req/s, p95 94 ms | 2.7 / 3.2 / 3.3 ms; 964 req/s, p95 25 ms |
| holders, busiest ARC-200, latest | 0.9 / 1.8 / 3.0 ms; 3,888 req/s, p95 8 ms | 1.2 / 1.3 / 1.4 ms; 3,037 req/s, p95 8 ms |
| token info | 0.3 / 0.3 / 0.4 ms; 19,970 req/s, p95 1 ms | 0.5 / 0.6 / 0.6 ms; 6,761 req/s, p95 4 ms |

