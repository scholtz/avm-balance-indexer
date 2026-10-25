# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-25 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,303,998 to 66,313,997) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 77 rounds/s | 145 rounds/s |
| Effects written | 883,027 (6,837/s) | 883,027 (12,811/s) |
| Time for the range | 129.1 s | 68.9 s |
| Stored size | 140.6 MiB | 585.5 MiB |
| Per round | 14.39 KiB | 59.96 KiB |
| Per effect | 164 bytes | 685 bytes |
| If all 66 M MainNet rounds were like these | 0.9 TiB | 3.7 TiB |

Block download is the same for both and not part of the table: 141 rounds/s from the public archival nodes (259,058 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 71.0 MiB, effects_by_token_bucket 56.9 MiB, effect_buckets_by_account_token 4.7 MiB, holdings_by_token 2.3 MiB, accounts_touched_by_token 2.3 MiB
- PostgreSQL 17.11: effects_by_account_token 315.3 MiB, effects_by_token_bucket 236.0 MiB, holdings_by_token 10.3 MiB, accounts_touched_by_token 10.2 MiB, holdings_by_account 9.4 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 4 ARC-200 tokens, 11,200 ASAs. 4,458 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 625,218 | 5,321 | 17,280 |
| asa:31566704 |  | 68,087 | 375 | 6,764 |
| asa:127746157 |  | 54,060 | 0 | 1 |
| asa:849191641 |  | 29,829 | 1,680 | 5,950 |
| asa:127745593 |  | 29,379 | 0 | 1 |
| asa:127746786 |  | 19,527 | 0 | 1 |
| asa:3145862805 |  | 13,355 | 1,725 | 1,726 |
| asa:1284444444 |  | 5,403 | 14 | 21 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1294765516 |  | 1,008 | 1 | 2 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.5 / 2.2 ms; 3,094 req/s, p95 10 ms | 0.6 / 0.8 / 1.3 ms; 4,545 req/s, p95 5 ms |
| account holdings | 1.9 / 3.6 / 7.8 ms; 1,833 req/s, p95 16 ms | 1.5 / 2.1 / 2.9 ms; 2,094 req/s, p95 12 ms |
| balance, latest | 3.1 / 4.5 / 6.9 ms; 1,379 req/s, p95 18 ms | 2.3 / 2.9 / 3.1 ms; 1,476 req/s, p95 17 ms |
| balance, past round | 2.8 / 3.2 / 3.8 ms; 1,659 req/s, p95 15 ms | 1.8 / 2.3 / 2.8 ms; 1,679 req/s, p95 14 ms |
| history, busiest account | 6.5 / 8.8 / 10.2 ms; 617 req/s, p95 49 ms | 4.7 / 6.3 / 7.5 ms; 675 req/s, p95 41 ms |
| history, typical account | 1.8 / 2.1 / 2.5 ms; 2,720 req/s, p95 9 ms | 1.6 / 2.1 / 2.4 ms; 2,129 req/s, p95 14 ms |
| holders, native, latest | 15.5 / 17.1 / 21.1 ms; 256 req/s, p95 75 ms | 2.4 / 3.3 / 4.1 ms; 1,161 req/s, p95 20 ms |
| holders, native, past round | 44.1 / 52.9 / 55.7 ms; 53 req/s, p95 358 ms | 8.1 / 9.0 / 9.8 ms; 289 req/s, p95 84 ms |
| holders, busiest ASA, latest | 89.7 / 92.6 / 101.7 ms; 58 req/s, p95 326 ms | 2.6 / 3.3 / 4.4 ms; 952 req/s, p95 23 ms |
| holders, busiest ARC-200, latest | 2.1 / 2.4 / 3.0 ms; 1,688 req/s, p95 15 ms | 1.1 / 1.3 / 1.5 ms; 2,853 req/s, p95 9 ms |
| token info | 0.7 / 0.9 / 1.3 ms; 4,913 req/s, p95 5 ms | 0.5 / 0.7 / 0.8 ms; 6,239 req/s, p95 4 ms |

