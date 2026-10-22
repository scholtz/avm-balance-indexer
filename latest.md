# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-22 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,210,016 to 66,220,015) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 86 rounds/s | 216 rounds/s |
| Effects written | 833,379 (7,150/s) | 833,379 (18,035/s) |
| Time for the range | 116.6 s | 46.2 s |
| Stored size | 132.6 MiB | 545.4 MiB |
| Per round | 13.58 KiB | 55.85 KiB |
| Per effect | 164 bytes | 676 bytes |
| If all 66 M MainNet rounds were like these | 0.8 TiB | 3.4 TiB |

Block download is the same for both and not part of the table: 247 rounds/s from the public archival nodes (250,764 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 67.7 MiB, effects_by_token_bucket 55.2 MiB, effect_buckets_by_account_token 3.7 MiB, holdings_by_token 1.8 MiB, accounts_touched_by_token 1.8 MiB
- PostgreSQL 17.11: effects_by_account_token 297.0 MiB, effects_by_token_bucket 222.6 MiB, holdings_by_token 8.2 MiB, accounts_touched_by_token 8.2 MiB, holdings_by_account 7.4 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 7 ARC-200 tokens, 4,862 ASAs. 2,315 of those ASAs were created inside the range, 1 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 591,618 | 4,618 | 14,201 |
| asa:127746157 |  | 54,239 | 0 | 1 |
| asa:31566704 |  | 51,031 | 434 | 3,833 |
| asa:849191641 |  | 34,137 | 1,905 | 6,471 |
| asa:127745593 |  | 32,291 | 0 | 1 |
| asa:127746786 |  | 16,689 | 0 | 1 |
| asa:3145862805 |  | 11,778 | 1,680 | 1,681 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,639 | 21 | 53 |
| asa:452399768 |  | 1,359 | 69 | 86 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.1 / 1.4 / 3.0 ms; 3,175 req/s, p95 9 ms | 0.6 / 0.6 / 0.7 ms; 4,839 req/s, p95 5 ms |
| account holdings | 2.2 / 4.4 / 7.2 ms; 1,354 req/s, p95 21 ms | 1.5 / 2.1 / 3.2 ms; 2,190 req/s, p95 11 ms |
| balance, latest | 2.9 / 4.7 / 7.0 ms; 1,232 req/s, p95 23 ms | 2.2 / 3.1 / 4.9 ms; 1,548 req/s, p95 16 ms |
| balance, past round | 2.6 / 3.2 / 4.6 ms; 1,731 req/s, p95 14 ms | 1.8 / 2.5 / 3.2 ms; 1,711 req/s, p95 14 ms |
| history, busiest account | 4.7 / 9.1 / 11.4 ms; 725 req/s, p95 40 ms | 4.4 / 6.2 / 7.2 ms; 882 req/s, p95 26 ms |
| history, typical account | 1.8 / 2.1 / 2.6 ms; 2,530 req/s, p95 12 ms | 1.6 / 2.0 / 2.2 ms; 2,066 req/s, p95 13 ms |
| holders, native, latest | 12.7 / 14.9 / 18.2 ms; 301 req/s, p95 66 ms | 2.3 / 3.2 / 4.0 ms; 1,148 req/s, p95 20 ms |
| holders, native, past round | 39.3 / 46.5 / 54.6 ms; 56 req/s, p95 354 ms | 7.9 / 10.0 / 10.8 ms; 288 req/s, p95 84 ms |
| holders, busiest ASA, latest | 2.0 / 2.7 / 3.7 ms; 1,860 req/s, p95 13 ms | 1.2 / 1.4 / 1.6 ms; 2,967 req/s, p95 8 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.3 / 2.8 ms; 1,737 req/s, p95 14 ms | 1.2 / 1.3 / 1.7 ms; 2,741 req/s, p95 10 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 6,632 req/s, p95 4 ms | 0.5 / 0.5 / 0.6 ms; 7,199 req/s, p95 4 ms |

