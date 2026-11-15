# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-15 at commit `78b4cdb9`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,964,030 to 66,974,029) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 81 rounds/s | 229 rounds/s |
| Effects written | 847,035 (6,875/s) | 847,035 (19,407/s) |
| Time for the range | 123.2 s | 43.6 s |
| Stored size | 141.2 MiB | 565.2 MiB |
| Per round | 14.46 KiB | 57.88 KiB |
| Per effect | 171 bytes | 685 bytes |
| If all 66 M MainNet rounds were like these | 0.9 TiB | 3.6 TiB |

Block download is the same for both and not part of the table: 297 rounds/s from the public archival nodes (248,132 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 71.1 MiB, effects_by_token_bucket 57.5 MiB, effect_buckets_by_account_token 4.7 MiB, holdings_by_token 2.4 MiB, accounts_touched_by_token 2.3 MiB
- PostgreSQL 17.12: effects_by_account_token 303.8 MiB, effects_by_token_bucket 227.1 MiB, accounts_touched_by_token 10.5 MiB, holdings_by_token 10.5 MiB, holdings_by_account 9.5 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 9,928 ASAs. 4,207 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 565,950 | 5,478 | 16,137 |
| asa:127746157 |  | 53,074 | 0 | 1 |
| asa:849191641 |  | 50,608 | 2,841 | 8,999 |
| asa:31566704 |  | 50,477 | 459 | 5,664 |
| asa:127745593 |  | 33,493 | 0 | 1 |
| asa:127746786 |  | 17,293 | 0 | 1 |
| asa:3737581431 |  | 4,800 | 23 | 24 |
| asa:1284444444 |  | 4,587 | 18 | 61 |
| asa:2494786278 |  | 4,283 | 83 | 100 |
| asa:3178895177 |  | 2,231 | 153 | 189 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 2.5 / 3.4 ms; 3,213 req/s, p95 9 ms | 0.6 / 0.7 / 0.8 ms; 4,787 req/s, p95 5 ms |
| account holdings | 1.8 / 3.1 / 4.3 ms; 1,821 req/s, p95 15 ms | 1.4 / 2.2 / 4.3 ms; 2,154 req/s, p95 12 ms |
| balance, latest | 2.8 / 4.6 / 6.1 ms; 1,534 req/s, p95 16 ms | 2.2 / 2.7 / 2.9 ms; 1,499 req/s, p95 17 ms |
| balance, past round | 2.5 / 3.0 / 3.5 ms; 1,578 req/s, p95 16 ms | 1.7 / 2.6 / 3.4 ms; 1,725 req/s, p95 13 ms |
| history, busiest account | 6.4 / 11.4 / 15.7 ms; 689 req/s, p95 44 ms | 3.8 / 5.7 / 6.6 ms; 909 req/s, p95 26 ms |
| history, typical account | 1.6 / 2.0 / 2.4 ms; 2,416 req/s, p95 12 ms | 1.5 / 2.6 / 3.9 ms; 2,136 req/s, p95 12 ms |
| holders, native, latest | 12.7 / 17.0 / 25.4 ms; 293 req/s, p95 70 ms | 2.2 / 3.1 / 3.9 ms; 1,207 req/s, p95 19 ms |
| holders, native, past round | 33.4 / 44.2 / 65.6 ms; 63 req/s, p95 294 ms | 7.2 / 8.0 / 10.2 ms; 312 req/s, p95 76 ms |
| holders, busiest ASA, latest | 1.9 / 2.3 / 3.1 ms; 1,977 req/s, p95 12 ms | 1.2 / 1.3 / 1.8 ms; 2,864 req/s, p95 8 ms |
| token info | 0.6 / 0.7 / 1.0 ms; 6,704 req/s, p95 4 ms | 0.5 / 0.6 / 1.2 ms; 6,196 req/s, p95 5 ms |

## voi

Measured on 2026-11-15 at commit `78b4cdb9`: the 100,000 newest rounds of voi (chain rounds 24,454,862 to 24,554,861) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 5,258 rounds/s | 10,349 rounds/s |
| Effects written | 20,581 (1,082/s) | 20,581 (2,130/s) |
| Time for the range | 19.0 s | 9.7 s |
| Stored size | 2.1 MiB | 14.9 MiB |
| Per round | 0.02 KiB | 0.15 KiB |
| Per effect | 105 bytes | 757 bytes |

Block download is the same for both and not part of the table: 71 rounds/s from the public archival nodes (6,344 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.2 MiB, effects_by_token_bucket 0.8 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, holdings_by_account 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 9.2 MiB, effects_by_token_bucket 5.3 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 15 ARC-200 tokens, 5 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 19,407 | 60 | 87 |
| asa:586896 |  | 374 | 1 | 2 |
| arc200:420069 |  | 218 | 4 | 11 |
| arc200:390001 |  | 112 | 6 | 15 |
| arc200:300279 |  | 70 | 17 | 20 |
| arc200:404004 |  | 66 | 16 | 17 |
| arc200:411311 |  | 66 | 16 | 17 |
| arc200:416000 |  | 66 | 16 | 17 |
| arc200:395614 |  | 60 | 4 | 8 |
| arc200:420019 |  | 60 | 15 | 16 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 1.2 / 2.2 ms; 3,283 req/s, p95 9 ms | 0.6 / 0.7 / 0.9 ms; 4,921 req/s, p95 5 ms |
| account holdings | 1.7 / 2.1 / 2.9 ms; 1,858 req/s, p95 15 ms | 1.4 / 1.7 / 2.6 ms; 2,273 req/s, p95 11 ms |
| balance, latest | 2.7 / 3.4 / 4.2 ms; 1,602 req/s, p95 16 ms | 2.1 / 2.9 / 4.0 ms; 1,676 req/s, p95 14 ms |
| balance, past round | 2.5 / 2.9 / 3.6 ms; 1,641 req/s, p95 15 ms | 1.7 / 2.1 / 2.7 ms; 1,837 req/s, p95 13 ms |
| history, busiest account | 6.8 / 9.1 / 10.3 ms; 635 req/s, p95 47 ms | 4.5 / 6.7 / 7.4 ms; 627 req/s, p95 42 ms |
| history, typical account | 1.7 / 3.0 / 3.6 ms; 1,959 req/s, p95 14 ms | 1.4 / 2.8 / 3.7 ms; 2,031 req/s, p95 12 ms |
| holders, native, latest | 3.2 / 4.4 / 6.3 ms; 810 req/s, p95 32 ms | 1.8 / 2.6 / 3.9 ms; 1,684 req/s, p95 13 ms |
| holders, native, past round | 9.1 / 14.2 / 16.0 ms; 188 req/s, p95 112 ms | 6.1 / 6.7 / 6.7 ms; 371 req/s, p95 62 ms |
| holders, busiest ASA, latest | 2.0 / 2.7 / 3.4 ms; 1,629 req/s, p95 16 ms | 1.2 / 1.3 / 1.3 ms; 3,043 req/s, p95 7 ms |
| holders, busiest ARC-200, latest | 2.1 / 2.6 / 3.2 ms; 1,499 req/s, p95 16 ms | 1.2 / 1.9 / 2.4 ms; 2,714 req/s, p95 8 ms |
| token info | 0.6 / 0.7 / 0.8 ms; 6,124 req/s, p95 4 ms | 0.4 / 0.5 / 0.5 ms; 7,139 req/s, p95 4 ms |

