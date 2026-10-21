# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-21 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,178,755 to 66,188,754) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 71 rounds/s | 206 rounds/s |
| Effects written | 1,154,396 (8,155/s) | 1,154,396 (23,756/s) |
| Time for the range | 141.6 s | 48.6 s |
| Stored size | 179.2 MiB | 749.7 MiB |
| Per round | 18.35 KiB | 76.77 KiB |
| Per effect | 161 bytes | 674 bytes |
| If all 66 M MainNet rounds were like these | 1.1 TiB | 4.7 TiB |

Block download is the same for both and not part of the table: 172 rounds/s from the public archival nodes (393,714 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 91.6 MiB, effects_by_token_bucket 75.6 MiB, effect_buckets_by_account_token 4.4 MiB, holdings_by_token 2.2 MiB, accounts_touched_by_token 2.2 MiB
- PostgreSQL 17.11: effects_by_account_token 411.5 MiB, effects_by_token_bucket 307.9 MiB, holdings_by_token 9.3 MiB, accounts_touched_by_token 9.3 MiB, holdings_by_account 8.6 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 1 ARC-200 tokens, 7,510 ASAs. 5,033 of those ASAs were created inside the range, 1 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 883,058 | 5,457 | 16,024 |
| asa:31566704 |  | 76,497 | 488 | 4,980 |
| asa:127746157 |  | 54,730 | 0 | 1 |
| asa:849191641 |  | 32,474 | 1,856 | 6,314 |
| asa:127745593 |  | 31,807 | 0 | 1 |
| asa:127746786 |  | 16,678 | 0 | 1 |
| asa:3145862805 |  | 12,227 | 1,758 | 1,759 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,440 | 21 | 33 |
| asa:452399768 |  | 1,393 | 69 | 91 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.3 / 2.0 ms; 2,849 req/s, p95 11 ms | 0.7 / 0.8 / 0.9 ms; 4,856 req/s, p95 5 ms |
| account holdings | 2.1 / 3.3 / 5.1 ms; 1,070 req/s, p95 36 ms | 1.6 / 1.9 / 3.6 ms; 2,161 req/s, p95 12 ms |
| balance, latest | 3.2 / 5.2 / 8.0 ms; 1,379 req/s, p95 19 ms | 2.3 / 2.6 / 2.9 ms; 1,631 req/s, p95 15 ms |
| balance, past round | 2.7 / 3.2 / 3.6 ms; 1,776 req/s, p95 13 ms | 1.9 / 2.6 / 3.7 ms; 1,755 req/s, p95 14 ms |
| history, busiest account | 6.3 / 8.8 / 11.6 ms; 669 req/s, p95 44 ms | 4.8 / 6.1 / 7.1 ms; 776 req/s, p95 37 ms |
| history, typical account | 1.8 / 2.2 / 2.7 ms; 2,659 req/s, p95 10 ms | 1.6 / 1.9 / 2.3 ms; 2,388 req/s, p95 11 ms |
| holders, native, latest | 13.9 / 16.2 / 18.7 ms; 278 req/s, p95 72 ms | 2.5 / 3.4 / 4.2 ms; 1,229 req/s, p95 19 ms |
| holders, native, past round | 39.2 / 46.2 / 51.4 ms; 63 req/s, p95 305 ms | 7.0 / 7.8 / 8.4 ms; 342 req/s, p95 73 ms |
| holders, busiest ASA, latest | 54.6 / 57.6 / 64.5 ms; 94 req/s, p95 204 ms | 2.6 / 3.1 / 3.2 ms; 1,087 req/s, p95 20 ms |
| holders, busiest ARC-200, latest | 2.1 / 2.5 / 3.0 ms; 1,684 req/s, p95 16 ms | 1.2 / 1.5 / 2.3 ms; 3,452 req/s, p95 7 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 6,375 req/s, p95 4 ms | 0.5 / 0.6 / 0.6 ms; 7,937 req/s, p95 3 ms |

