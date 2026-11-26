# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-26 at commit `ee61ce36`: the 10,000 newest rounds of algorand-mainnet (chain rounds 67,307,608 to 67,317,607) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 134 rounds/s | 241 rounds/s |
| Effects written | 676,739 (9,057/s) | 676,739 (16,323/s) |
| Time for the range | 74.7 s | 41.5 s |
| Stored size | 120.3 MiB | 463.9 MiB |
| Per round | 12.31 KiB | 47.50 KiB |
| Per effect | 181 bytes | 698 bytes |
| If all 66 M MainNet rounds were like these | 0.8 TiB | 2.9 TiB |

Block download is the same for both and not part of the table: 264 rounds/s from the public archival nodes (192,869 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 58.8 MiB, effects_by_token_bucket 46.6 MiB, effect_buckets_by_account_token 5.7 MiB, holdings_by_token 2.8 MiB, accounts_touched_by_token 2.7 MiB
- PostgreSQL 17.12: effects_by_account_token 242.2 MiB, effects_by_token_bucket 182.1 MiB, holdings_by_token 12.1 MiB, accounts_touched_by_token 12.1 MiB, holdings_by_account 11.0 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 6 ARC-200 tokens, 11,287 ASAs. 4,433 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 466,486 | 6,016 | 18,685 |
| asa:849191641 |  | 54,867 | 3,948 | 11,018 |
| asa:127746157 |  | 34,405 | 0 | 1 |
| asa:3145862805 |  | 20,435 | 2,999 | 3,000 |
| asa:31566704 |  | 19,996 | 391 | 6,373 |
| asa:127745593 |  | 19,888 | 0 | 1 |
| asa:127746786 |  | 10,268 | 0 | 1 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,242 | 13 | 24 |
| asa:2543151056 |  | 1,624 | 414 | 415 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.1 / 2.7 / 4.3 ms; 4,133 req/s, p95 8 ms | 0.6 / 0.8 / 1.0 ms; 3,472 req/s, p95 9 ms |
| account holdings | 1.5 / 2.5 / 3.6 ms; 2,273 req/s, p95 12 ms | 1.4 / 2.0 / 2.9 ms; 2,132 req/s, p95 12 ms |
| balance, latest | 2.4 / 3.7 / 5.0 ms; 1,799 req/s, p95 14 ms | 2.2 / 3.0 / 3.7 ms; 1,419 req/s, p95 17 ms |
| balance, past round | 2.1 / 2.5 / 3.0 ms; 1,993 req/s, p95 13 ms | 1.8 / 2.6 / 2.9 ms; 1,644 req/s, p95 14 ms |
| history, busiest account | 6.7 / 11.5 / 26.8 ms; 545 req/s, p95 62 ms | 5.1 / 8.0 / 8.7 ms; 524 req/s, p95 51 ms |
| history, typical account | 1.3 / 1.6 / 2.0 ms; 3,255 req/s, p95 9 ms | 1.7 / 2.1 / 2.5 ms; 2,282 req/s, p95 11 ms |
| holders, native, latest | 9.8 / 13.0 / 15.0 ms; 393 req/s, p95 53 ms | 2.1 / 2.8 / 3.4 ms; 1,153 req/s, p95 22 ms |
| holders, native, past round | 18.2 / 24.2 / 28.8 ms; 106 req/s, p95 183 ms | 7.5 / 8.3 / 11.7 ms; 308 req/s, p95 78 ms |
| holders, busiest ASA, latest | 8.8 / 9.8 / 11.7 ms; 425 req/s, p95 49 ms | 2.0 / 2.5 / 3.3 ms; 1,191 req/s, p95 20 ms |
| holders, busiest ARC-200, latest | 1.4 / 1.7 / 2.1 ms; 2,561 req/s, p95 10 ms | 1.2 / 1.4 / 1.8 ms; 2,909 req/s, p95 7 ms |
| token info | 0.5 / 0.6 / 0.6 ms; 8,504 req/s, p95 4 ms | 0.5 / 0.6 / 0.6 ms; 6,261 req/s, p95 4 ms |

## voi

Measured on 2026-11-26 at commit `ee61ce36`: the 100,000 newest rounds of voi (chain rounds 24,797,697 to 24,897,696) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 1,367 rounds/s | 6,578 rounds/s |
| Effects written | 130,344 (1,782/s) | 130,344 (8,575/s) |
| Time for the range | 73.2 s | 15.2 s |
| Stored size | 8.1 MiB | 71.9 MiB |
| Per round | 0.08 KiB | 0.74 KiB |
| Per effect | 65 bytes | 577 bytes |

Block download is the same for both and not part of the table: 53 rounds/s from the public archival nodes (11,263 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 4.7 MiB, effects_by_token_bucket 3.1 MiB, effect_buckets_by_account_token 0.1 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 40.7 MiB, effects_by_token_bucket 30.3 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 47 ARC-200 tokens, 7 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 121,520 | 159 | 300 |
| arc200:390001 |  | 2,676 | 17 | 38 |
| arc200:420069 |  | 1,044 | 11 | 24 |
| arc200:395614 |  | 822 | 11 | 16 |
| arc200:47138068 |  | 696 | 11 | 18 |
| arc200:300279 |  | 594 | 26 | 34 |
| arc200:40153155 |  | 410 | 18 | 22 |
| arc200:410419 |  | 342 | 3 | 12 |
| arc200:410811 |  | 318 | 11 | 15 |
| arc200:404004 |  | 216 | 20 | 26 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 1.3 / 1.7 ms; 3,953 req/s, p95 8 ms | 0.6 / 0.8 / 0.9 ms; 4,121 req/s, p95 7 ms |
| account holdings | 1.3 / 1.5 / 2.2 ms; 2,127 req/s, p95 14 ms | 1.4 / 1.6 / 2.3 ms; 2,151 req/s, p95 12 ms |
| balance, latest | 2.2 / 2.8 / 3.3 ms; 1,822 req/s, p95 14 ms | 2.1 / 2.6 / 3.1 ms; 1,503 req/s, p95 16 ms |
| balance, past round | 2.2 / 2.6 / 3.1 ms; 1,978 req/s, p95 12 ms | 1.8 / 2.3 / 3.0 ms; 1,663 req/s, p95 14 ms |
| history, busiest account | 6.4 / 8.6 / 9.5 ms; 628 req/s, p95 58 ms | 5.4 / 7.6 / 8.8 ms; 525 req/s, p95 52 ms |
| history, typical account | 1.5 / 3.1 / 6.0 ms; 2,661 req/s, p95 11 ms | 1.5 / 2.5 / 2.9 ms; 1,885 req/s, p95 14 ms |
| holders, native, latest | 6.0 / 7.5 / 10.0 ms; 565 req/s, p95 38 ms | 2.2 / 2.9 / 4.3 ms; 1,227 req/s, p95 18 ms |
| holders, native, past round | 15.4 / 19.8 / 23.1 ms; 125 req/s, p95 154 ms | 7.8 / 8.5 / 9.1 ms; 290 req/s, p95 79 ms |
| holders, busiest ASA, latest | 1.4 / 1.8 / 2.2 ms; 2,733 req/s, p95 9 ms | 1.1 / 1.2 / 1.3 ms; 3,038 req/s, p95 7 ms |
| holders, busiest ARC-200, latest | 1.7 / 3.0 / 3.8 ms; 1,727 req/s, p95 15 ms | 1.3 / 1.7 / 1.9 ms; 2,307 req/s, p95 9 ms |
| token info | 0.5 / 0.6 / 0.6 ms; 8,046 req/s, p95 4 ms | 0.5 / 0.6 / 0.7 ms; 6,532 req/s, p95 4 ms |

