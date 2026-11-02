# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-02 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,556,438 to 66,566,437) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 120 rounds/s | 399 rounds/s |
| Effects written | 420,856 (5,034/s) | 420,856 (16,780/s) |
| Time for the range | 83.6 s | 25.1 s |
| Stored size | 79.8 MiB | 297.1 MiB |
| Per round | 8.17 KiB | 30.43 KiB |
| Per effect | 191 bytes | 710 bytes |
| If all 66 M MainNet rounds were like these | 0.5 TiB | 1.9 TiB |

Block download is the same for both and not part of the table: 89 rounds/s from the public archival nodes (107,193 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 39.3 MiB, effects_by_token_bucket 29.0 MiB, effect_buckets_by_account_token 4.3 MiB, holdings_by_token 2.2 MiB, accounts_touched_by_token 2.2 MiB
- PostgreSQL 17.11: effects_by_account_token 153.4 MiB, effects_by_token_bucket 114.0 MiB, holdings_by_token 9.3 MiB, accounts_touched_by_token 9.2 MiB, holdings_by_account 8.4 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 1 ARC-200 tokens, 8,270 ASAs. 3,844 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 293,288 | 5,443 | 15,691 |
| asa:849191641 |  | 41,192 | 2,745 | 8,665 |
| asa:31566704 |  | 23,068 | 384 | 4,151 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,568 | 22 | 36 |
| asa:2327984798 |  | 3,956 | 40 | 65 |
| asa:712012773 |  | 1,746 | 49 | 63 |
| asa:3148132803 |  | 1,636 | 162 | 172 |
| asa:2200000000 |  | 1,215 | 47 | 100 |
| asa:452399768 |  | 1,194 | 61 | 76 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.4 / 2.0 ms; 3,046 req/s, p95 9 ms | 0.5 / 0.6 / 0.6 ms; 6,331 req/s, p95 4 ms |
| account holdings | 2.0 / 3.8 / 4.7 ms; 1,350 req/s, p95 26 ms | 1.3 / 1.9 / 3.1 ms; 2,537 req/s, p95 10 ms |
| balance, latest | 3.0 / 7.3 / 15.0 ms; 1,361 req/s, p95 20 ms | 2.0 / 2.7 / 3.9 ms; 1,838 req/s, p95 14 ms |
| balance, past round | 2.6 / 3.0 / 3.7 ms; 1,736 req/s, p95 15 ms | 1.6 / 2.6 / 4.0 ms; 2,103 req/s, p95 12 ms |
| history, busiest account | 5.3 / 11.5 / 18.6 ms; 717 req/s, p95 43 ms | 3.5 / 5.7 / 8.2 ms; 674 req/s, p95 36 ms |
| history, typical account | 1.7 / 1.9 / 2.2 ms; 2,902 req/s, p95 10 ms | 1.3 / 1.7 / 2.2 ms; 2,399 req/s, p95 13 ms |
| holders, native, latest | 10.4 / 11.8 / 13.1 ms; 324 req/s, p95 69 ms | 1.9 / 2.7 / 3.5 ms; 1,494 req/s, p95 16 ms |
| holders, native, past round | 35.7 / 40.3 / 43.1 ms; 59 req/s, p95 329 ms | 6.5 / 7.4 / 8.4 ms; 329 req/s, p95 72 ms |
| holders, busiest ASA, latest | 14.4 / 17.2 / 22.4 ms; 274 req/s, p95 70 ms | 1.6 / 2.0 / 2.3 ms; 1,537 req/s, p95 15 ms |
| holders, busiest ARC-200, latest | 1.9 / 2.3 / 2.7 ms; 1,707 req/s, p95 15 ms | 1.0 / 1.1 / 1.5 ms; 3,727 req/s, p95 7 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 6,676 req/s, p95 4 ms | 0.3 / 0.4 / 0.6 ms; 10,245 req/s, p95 3 ms |

## voi

Measured on 2026-11-02 at commit `9e2e5bda`: the 100,000 newest rounds of voi (chain rounds 24,048,626 to 24,148,625) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 4,799 rounds/s | 14,483 rounds/s |
| Effects written | 20,883 (1,002/s) | 20,883 (3,025/s) |
| Time for the range | 20.8 s | 6.9 s |
| Stored size | 1.9 MiB | 14.2 MiB |
| Per round | 0.02 KiB | 0.15 KiB |
| Per effect | 93 bytes | 709 bytes |

Block download is the same for both and not part of the table: 29 rounds/s from the public archival nodes (5,418 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.0 MiB, effects_by_token_bucket 0.7 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, holdings_by_account 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 8.5 MiB, effects_by_token_bucket 5.1 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 10 ARC-200 tokens, 3 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 19,366 | 41 | 63 |
| asa:586896 |  | 998 | 1 | 2 |
| arc200:300279 |  | 74 | 14 | 17 |
| arc200:420069 |  | 72 | 3 | 9 |
| arc200:404004 |  | 66 | 13 | 14 |
| arc200:411311 |  | 66 | 13 | 14 |
| arc200:416000 |  | 66 | 13 | 14 |
| arc200:420019 |  | 66 | 13 | 14 |
| arc200:390001 |  | 48 | 5 | 7 |
| arc200:395614 |  | 28 | 2 | 6 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 1.1 / 2.1 ms; 3,468 req/s, p95 8 ms | 0.4 / 0.5 / 0.6 ms; 6,451 req/s, p95 4 ms |
| account holdings | 1.8 / 2.6 / 3.8 ms; 2,017 req/s, p95 14 ms | 1.2 / 1.6 / 2.5 ms; 3,108 req/s, p95 8 ms |
| balance, latest | 2.7 / 3.1 / 3.4 ms; 1,565 req/s, p95 17 ms | 1.7 / 2.3 / 2.9 ms; 2,073 req/s, p95 12 ms |
| balance, past round | 2.6 / 3.0 / 3.5 ms; 1,750 req/s, p95 14 ms | 1.5 / 2.2 / 2.7 ms; 2,273 req/s, p95 10 ms |
| history, busiest account | 5.7 / 9.1 / 13.5 ms; 764 req/s, p95 37 ms | 3.8 / 5.3 / 6.2 ms; 1,043 req/s, p95 22 ms |
| history, typical account | 1.8 / 3.3 / 6.4 ms; 1,469 req/s, p95 20 ms | 1.2 / 2.2 / 2.9 ms; 2,209 req/s, p95 12 ms |
| holders, native, latest | 2.8 / 4.1 / 5.2 ms; 940 req/s, p95 29 ms | 1.3 / 1.8 / 2.9 ms; 2,633 req/s, p95 9 ms |
| holders, native, past round | 7.3 / 10.9 / 14.8 ms; 231 req/s, p95 95 ms | 4.8 / 5.6 / 12.7 ms; 446 req/s, p95 59 ms |
| holders, busiest ASA, latest | 1.9 / 2.3 / 2.5 ms; 1,522 req/s, p95 18 ms | 0.9 / 1.1 / 1.3 ms; 4,456 req/s, p95 5 ms |
| holders, busiest ARC-200, latest | 2.3 / 3.1 / 4.0 ms; 1,038 req/s, p95 27 ms | 1.0 / 1.4 / 1.6 ms; 3,526 req/s, p95 7 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 7,217 req/s, p95 4 ms | 0.2 / 0.3 / 0.4 ms; 9,629 req/s, p95 3 ms |

