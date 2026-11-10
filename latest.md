# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-10 at commit `e3803b2e`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,807,854 to 66,817,853) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 118 rounds/s | 345 rounds/s |
| Effects written | 426,991 (5,032/s) | 426,991 (14,712/s) |
| Time for the range | 84.9 s | 29.0 s |
| Stored size | 78.5 MiB | 297.9 MiB |
| Per round | 8.04 KiB | 30.50 KiB |
| Per effect | 185 bytes | 703 bytes |
| If all 66 M MainNet rounds were like these | 0.5 TiB | 1.9 TiB |

Block download is the same for both and not part of the table: 312 rounds/s from the public archival nodes (118,948 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 38.9 MiB, effects_by_token_bucket 29.5 MiB, effect_buckets_by_account_token 3.9 MiB, holdings_by_token 1.9 MiB, accounts_touched_by_token 1.9 MiB
- PostgreSQL 17.11: effects_by_account_token 155.8 MiB, effects_by_token_bucket 115.6 MiB, holdings_by_token 8.2 MiB, accounts_touched_by_token 8.1 MiB, holdings_by_account 7.6 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 5 ARC-200 tokens, 6,347 ASAs. 3,062 of those ASAs were created inside the range, 4 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 297,214 | 4,239 | 14,469 |
| asa:849191641 |  | 52,835 | 3,042 | 9,461 |
| asa:31566704 |  | 41,494 | 344 | 3,944 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,523 | 12 | 16 |
| asa:2543151056 |  | 1,576 | 402 | 403 |
| asa:3203964481 |  | 1,339 | 31 | 73 |
| asa:452399768 |  | 899 | 56 | 69 |
| asa:1134696561 |  | 838 | 19 | 31 |
| asa:2586641946 |  | 756 | 22 | 40 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.5 / 3.4 ms; 3,182 req/s, p95 8 ms | 0.7 / 0.8 / 0.8 ms; 4,130 req/s, p95 6 ms |
| account holdings | 2.0 / 3.6 / 4.2 ms; 1,598 req/s, p95 17 ms | 1.8 / 2.6 / 3.8 ms; 1,753 req/s, p95 14 ms |
| balance, latest | 3.3 / 9.5 / 12.6 ms; 1,404 req/s, p95 18 ms | 2.6 / 3.5 / 4.4 ms; 1,317 req/s, p95 18 ms |
| balance, past round | 2.5 / 2.9 / 3.6 ms; 1,811 req/s, p95 14 ms | 2.2 / 2.9 / 3.5 ms; 1,429 req/s, p95 17 ms |
| history, busiest account | 4.4 / 10.1 / 14.0 ms; 679 req/s, p95 42 ms | 5.6 / 8.4 / 8.8 ms; 794 req/s, p95 30 ms |
| history, typical account | 1.7 / 1.8 / 2.6 ms; 2,900 req/s, p95 9 ms | 1.7 / 2.4 / 3.2 ms; 1,926 req/s, p95 13 ms |
| holders, native, latest | 16.9 / 18.5 / 20.2 ms; 247 req/s, p95 79 ms | 3.2 / 4.1 / 4.8 ms; 828 req/s, p95 27 ms |
| holders, native, past round | 27.8 / 34.8 / 38.0 ms; 70 req/s, p95 287 ms | 9.0 / 9.8 / 11.1 ms; 256 req/s, p95 89 ms |
| holders, busiest ASA, latest | 13.9 / 15.4 / 16.9 ms; 286 req/s, p95 67 ms | 3.0 / 3.6 / 3.8 ms; 910 req/s, p95 24 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.6 / 3.3 ms; 1,777 req/s, p95 14 ms | 1.5 / 2.0 / 3.2 ms; 2,629 req/s, p95 9 ms |
| token info | 0.7 / 0.8 / 0.8 ms; 6,925 req/s, p95 4 ms | 0.6 / 0.7 / 1.1 ms; 5,699 req/s, p95 5 ms |

## voi

Measured on 2026-11-10 at commit `e3803b2e`: the 100,000 newest rounds of voi (chain rounds 24,299,067 to 24,399,066) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 4,584 rounds/s | 9,476 rounds/s |
| Effects written | 24,388 (1,118/s) | 24,388 (2,311/s) |
| Time for the range | 21.8 s | 10.6 s |
| Stored size | 2.4 MiB | 17.4 MiB |
| Per round | 0.03 KiB | 0.18 KiB |
| Per effect | 105 bytes | 746 bytes |

Block download is the same for both and not part of the table: 63 rounds/s from the public archival nodes (6,617 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.4 MiB, effects_by_token_bucket 0.9 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 10.7 MiB, effects_by_token_bucket 6.1 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 23 ARC-200 tokens, 4 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 20,849 | 61 | 99 |
| arc200:390001 |  | 888 | 11 | 21 |
| asa:586896 |  | 626 | 1 | 2 |
| arc200:395614 |  | 404 | 5 | 14 |
| arc200:420069 |  | 316 | 17 | 26 |
| arc200:300279 |  | 220 | 19 | 27 |
| arc200:47138068 |  | 180 | 7 | 11 |
| arc200:412682 |  | 166 | 6 | 13 |
| asa:302190 |  | 132 | 5 | 9 |
| arc200:410419 |  | 80 | 6 | 9 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.4 / 2.6 ms; 3,371 req/s, p95 9 ms | 0.7 / 1.2 / 2.6 ms; 4,055 req/s, p95 6 ms |
| account holdings | 1.7 / 2.5 / 3.9 ms; 1,848 req/s, p95 14 ms | 1.7 / 2.3 / 3.3 ms; 1,947 req/s, p95 12 ms |
| balance, latest | 2.7 / 3.3 / 4.4 ms; 1,510 req/s, p95 16 ms | 2.4 / 2.9 / 3.4 ms; 1,438 req/s, p95 17 ms |
| balance, past round | 2.5 / 3.0 / 3.6 ms; 1,391 req/s, p95 18 ms | 2.1 / 2.4 / 2.9 ms; 1,596 req/s, p95 15 ms |
| history, busiest account | 5.6 / 9.9 / 14.3 ms; 748 req/s, p95 38 ms | 5.7 / 8.4 / 9.2 ms; 726 req/s, p95 37 ms |
| history, typical account | 1.8 / 3.3 / 3.8 ms; 1,907 req/s, p95 14 ms | 1.6 / 2.9 / 3.3 ms; 1,732 req/s, p95 14 ms |
| holders, native, latest | 3.3 / 4.7 / 5.9 ms; 798 req/s, p95 33 ms | 2.3 / 3.0 / 3.5 ms; 1,256 req/s, p95 17 ms |
| holders, native, past round | 9.3 / 12.3 / 16.5 ms; 183 req/s, p95 113 ms | 7.3 / 8.4 / 10.0 ms; 319 req/s, p95 73 ms |
| holders, busiest ASA, latest | 2.0 / 2.5 / 2.8 ms; 1,637 req/s, p95 16 ms | 1.4 / 1.5 / 1.7 ms; 2,629 req/s, p95 8 ms |
| holders, busiest ARC-200, latest | 2.4 / 2.9 / 3.6 ms; 1,302 req/s, p95 19 ms | 1.6 / 1.9 / 2.3 ms; 2,143 req/s, p95 10 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 6,799 req/s, p95 4 ms | 0.5 / 0.6 / 0.6 ms; 6,613 req/s, p95 4 ms |

