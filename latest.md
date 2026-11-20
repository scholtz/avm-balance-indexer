# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-20 at commit `ee61ce36`: the 10,000 newest rounds of algorand-mainnet (chain rounds 67,120,447 to 67,130,446) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 103 rounds/s | 500 rounds/s |
| Effects written | 639,899 (6,573/s) | 639,899 (31,987/s) |
| Time for the range | 97.4 s | 20.0 s |
| Stored size | 105.5 MiB | 428.4 MiB |
| Per round | 10.81 KiB | 43.87 KiB |
| Per effect | 169 bytes | 686 bytes |
| If all 66 M MainNet rounds were like these | 0.7 TiB | 2.7 TiB |

Block download is the same for both and not part of the table: 316 rounds/s from the public archival nodes (193,768 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 53.5 MiB, effects_by_token_bucket 42.9 MiB, effect_buckets_by_account_token 3.6 MiB, holdings_by_token 1.8 MiB, accounts_touched_by_token 1.7 MiB
- PostgreSQL 17.12: effects_by_account_token 230.2 MiB, effects_by_token_bucket 171.5 MiB, holdings_by_token 8.4 MiB, accounts_touched_by_token 8.3 MiB, holdings_by_account 7.5 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 6,908 ASAs. 379 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 450,290 | 2,653 | 11,728 |
| asa:127746157 |  | 33,978 | 0 | 1 |
| asa:31566704 |  | 26,545 | 413 | 4,359 |
| asa:849191641 |  | 21,788 | 1,218 | 5,203 |
| asa:127745593 |  | 19,861 | 0 | 1 |
| asa:3145862805 |  | 17,096 | 2,607 | 2,608 |
| asa:127746786 |  | 11,422 | 0 | 1 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:3203964481 |  | 4,620 | 10 | 75 |
| asa:1134696561 |  | 4,594 | 27 | 51 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.3 / 3.0 / 5.2 ms; 2,714 req/s, p95 12 ms | 0.6 / 0.8 / 0.9 ms; 4,581 req/s, p95 7 ms |
| account holdings | 2.0 / 3.3 / 4.9 ms; 1,317 req/s, p95 21 ms | 1.2 / 1.4 / 1.5 ms; 2,417 req/s, p95 11 ms |
| balance, latest | 3.2 / 5.5 / 6.8 ms; 1,125 req/s, p95 24 ms | 1.8 / 2.2 / 2.7 ms; 1,739 req/s, p95 14 ms |
| balance, past round | 3.1 / 4.2 / 5.5 ms; 1,275 req/s, p95 20 ms | 1.7 / 2.0 / 2.5 ms; 1,802 req/s, p95 13 ms |
| history, busiest account | 8.5 / 12.5 / 17.6 ms; 573 req/s, p95 52 ms | 5.2 / 6.7 / 7.9 ms; 968 req/s, p95 27 ms |
| history, typical account | 1.7 / 1.9 / 2.3 ms; 2,371 req/s, p95 13 ms | 1.3 / 1.6 / 2.0 ms; 2,756 req/s, p95 9 ms |
| holders, native, latest | 18.4 / 20.2 / 24.8 ms; 227 req/s, p95 93 ms | 1.9 / 2.4 / 4.0 ms; 1,244 req/s, p95 20 ms |
| holders, native, past round | 42.5 / 53.1 / 68.1 ms; 48 req/s, p95 419 ms | 5.7 / 6.3 / 7.4 ms; 473 req/s, p95 51 ms |
| holders, busiest ASA, latest | 2.0 / 2.5 / 2.9 ms; 1,497 req/s, p95 18 ms | 0.9 / 1.1 / 1.1 ms; 3,849 req/s, p95 6 ms |
| token info | 0.8 / 1.5 / 3.1 ms; 6,817 req/s, p95 4 ms | 0.4 / 0.5 / 0.7 ms; 7,250 req/s, p95 4 ms |

## voi

Measured on 2026-11-20 at commit `ee61ce36`: the 100,000 newest rounds of voi (chain rounds 24,610,142 to 24,710,141) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 1,611 rounds/s | 8,567 rounds/s |
| Effects written | 149,527 (2,408/s) | 149,527 (12,810/s) |
| Time for the range | 62.1 s | 11.7 s |
| Stored size | 16.3 MiB | 84.7 MiB |
| Per round | 0.17 KiB | 0.87 KiB |
| Per effect | 114 bytes | 594 bytes |

Block download is the same for both and not part of the table: 62 rounds/s from the public archival nodes (56,279 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 8.6 MiB, effects_by_token_bucket 7.6 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_account 0.0 MiB, holdings_by_token 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 46.2 MiB, effects_by_token_bucket 37.9 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 14 ARC-200 tokens, 2 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 149,031 | 134 | 182 |
| asa:586896 |  | 122 | 1 | 2 |
| arc200:300279 |  | 60 | 14 | 15 |
| arc200:404004 |  | 60 | 14 | 15 |
| arc200:411311 |  | 60 | 14 | 15 |
| arc200:416000 |  | 60 | 14 | 15 |
| arc200:420019 |  | 60 | 14 | 15 |
| arc200:390001 |  | 18 | 3 | 7 |
| arc200:420069 |  | 16 | 3 | 7 |
| arc200:395614 |  | 14 | 3 | 6 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.3 / 1.4 ms; 3,260 req/s, p95 10 ms | 0.6 / 0.8 / 1.0 ms; 4,762 req/s, p95 6 ms |
| account holdings | 1.8 / 3.0 / 4.5 ms; 1,407 req/s, p95 21 ms | 1.2 / 1.5 / 2.0 ms; 2,533 req/s, p95 10 ms |
| balance, latest | 2.8 / 3.6 / 5.2 ms; 1,481 req/s, p95 16 ms | 1.8 / 2.1 / 2.5 ms; 1,819 req/s, p95 13 ms |
| balance, past round | 2.6 / 3.0 / 3.6 ms; 1,544 req/s, p95 16 ms | 1.6 / 2.0 / 2.4 ms; 1,854 req/s, p95 13 ms |
| history, busiest account | 5.0 / 7.0 / 8.4 ms; 611 req/s, p95 45 ms | 5.6 / 6.4 / 7.2 ms; 734 req/s, p95 45 ms |
| history, typical account | 1.8 / 3.3 / 4.1 ms; 2,353 req/s, p95 12 ms | 1.2 / 2.2 / 2.7 ms; 2,393 req/s, p95 10 ms |
| holders, native, latest | 4.8 / 6.6 / 10.9 ms; 503 req/s, p95 50 ms | 1.9 / 2.6 / 3.2 ms; 1,261 req/s, p95 19 ms |
| holders, native, past round | 15.3 / 18.5 / 20.8 ms; 89 req/s, p95 233 ms | 5.8 / 6.3 / 6.6 ms; 458 req/s, p95 52 ms |
| holders, busiest ASA, latest | 2.0 / 2.3 / 2.6 ms; 1,857 req/s, p95 13 ms | 0.9 / 1.1 / 1.2 ms; 3,790 req/s, p95 6 ms |
| holders, busiest ARC-200, latest | 2.3 / 3.2 / 4.0 ms; 1,310 req/s, p95 19 ms | 1.1 / 1.4 / 1.6 ms; 2,899 req/s, p95 8 ms |
| token info | 0.7 / 0.9 / 0.9 ms; 5,748 req/s, p95 5 ms | 0.4 / 0.4 / 0.5 ms; 8,992 req/s, p95 3 ms |

