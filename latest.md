# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-28 at commit `ee61ce36`: the 10,000 newest rounds of algorand-mainnet (chain rounds 67,369,631 to 67,379,630) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 105 rounds/s | 365 rounds/s |
| Effects written | 707,510 (7,417/s) | 707,510 (25,818/s) |
| Time for the range | 95.4 s | 27.4 s |
| Stored size | 128.4 MiB | 491.6 MiB |
| Per round | 13.15 KiB | 50.34 KiB |
| Per effect | 185 bytes | 709 bytes |
| If all 66 M MainNet rounds were like these | 0.8 TiB | 3.1 TiB |

Block download is the same for both and not part of the table: 265 rounds/s from the public archival nodes (196,392 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 61.6 MiB, effects_by_token_bucket 48.4 MiB, effect_buckets_by_account_token 6.7 MiB, holdings_by_token 3.4 MiB, accounts_touched_by_token 3.4 MiB
- PostgreSQL 17.12: effects_by_account_token 252.8 MiB, effects_by_token_bucket 189.7 MiB, holdings_by_token 14.5 MiB, accounts_touched_by_token 14.5 MiB, holdings_by_account 13.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 3 ARC-200 tokens, 17,928 ASAs. 10,104 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 495,466 | 8,155 | 20,710 |
| asa:849191641 |  | 54,979 | 3,789 | 10,347 |
| asa:127746157 |  | 33,435 | 0 | 1 |
| asa:31566704 |  | 22,233 | 233 | 9,492 |
| asa:127745593 |  | 20,801 | 0 | 1 |
| asa:3145862805 |  | 17,880 | 2,404 | 2,405 |
| asa:127746786 |  | 9,747 | 0 | 1 |
| asa:3737581431 |  | 4,800 | 23 | 24 |
| asa:1284444444 |  | 4,268 | 20 | 34 |
| asa:2543151056 |  | 1,636 | 417 | 418 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.3 / 2.3 / 2.9 ms; 3,094 req/s, p95 11 ms | 0.7 / 0.9 / 1.2 ms; 4,728 req/s, p95 6 ms |
| account holdings | 1.9 / 3.3 / 4.7 ms; 1,804 req/s, p95 16 ms | 1.3 / 1.5 / 1.9 ms; 2,364 req/s, p95 10 ms |
| balance, latest | 3.3 / 5.7 / 8.0 ms; 1,351 req/s, p95 19 ms | 1.8 / 2.2 / 2.9 ms; 1,691 req/s, p95 15 ms |
| balance, past round | 2.6 / 3.1 / 4.0 ms; 1,528 req/s, p95 16 ms | 1.7 / 2.0 / 3.0 ms; 1,873 req/s, p95 13 ms |
| history, busiest account | 7.8 / 13.0 / 18.5 ms; 626 req/s, p95 55 ms | 4.0 / 5.0 / 5.4 ms; 497 req/s, p95 52 ms |
| history, typical account | 1.5 / 1.7 / 2.1 ms; 3,005 req/s, p95 8 ms | 1.3 / 1.6 / 2.0 ms; 2,480 req/s, p95 10 ms |
| holders, native, latest | 9.6 / 12.7 / 17.6 ms; 367 req/s, p95 59 ms | 2.0 / 2.7 / 3.5 ms; 1,447 req/s, p95 16 ms |
| holders, native, past round | 29.8 / 37.0 / 43.7 ms; 78 req/s, p95 243 ms | 6.1 / 6.9 / 7.0 ms; 411 req/s, p95 58 ms |
| holders, busiest ASA, latest | 11.3 / 13.7 / 16.3 ms; 345 req/s, p95 61 ms | 1.9 / 2.3 / 2.6 ms; 1,450 req/s, p95 16 ms |
| holders, busiest ARC-200, latest | 1.7 / 2.4 / 3.0 ms; 2,232 req/s, p95 11 ms | 1.0 / 1.1 / 1.2 ms; 3,797 req/s, p95 6 ms |
| token info | 0.6 / 0.8 / 0.9 ms; 6,890 req/s, p95 4 ms | 0.4 / 0.5 / 0.5 ms; 7,724 req/s, p95 4 ms |

## voi

Measured on 2026-11-28 at commit `ee61ce36`: the 100,000 newest rounds of voi (chain rounds 24,860,320 to 24,960,319) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 4,181 rounds/s | 15,813 rounds/s |
| Effects written | 47,493 (1,986/s) | 47,493 (7,510/s) |
| Time for the range | 23.9 s | 6.3 s |
| Stored size | 5.0 MiB | 31.2 MiB |
| Per round | 0.05 KiB | 0.32 KiB |
| Per effect | 109 bytes | 683 bytes |

Block download is the same for both and not part of the table: 61 rounds/s from the public archival nodes (11,293 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 2.8 MiB, effects_by_token_bucket 1.9 MiB, effect_buckets_by_account_token 0.1 MiB, holdings_by_token 0.0 MiB, holdings_by_account 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 18.3 MiB, effects_by_token_bucket 12.0 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 44 ARC-200 tokens, 7 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 38,492 | 159 | 303 |
| arc200:390001 |  | 2,798 | 18 | 37 |
| arc200:420069 |  | 1,070 | 11 | 23 |
| arc200:395614 |  | 886 | 12 | 16 |
| arc200:47138068 |  | 667 | 11 | 18 |
| arc200:300279 |  | 616 | 19 | 31 |
| arc200:40153155 |  | 386 | 18 | 22 |
| arc200:410419 |  | 376 | 3 | 12 |
| arc200:410811 |  | 290 | 10 | 14 |
| arc200:412682 |  | 240 | 7 | 13 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 1.2 / 1.3 ms; 3,475 req/s, p95 9 ms | 0.6 / 0.8 / 1.1 ms; 4,936 req/s, p95 6 ms |
| account holdings | 1.8 / 2.1 / 2.9 ms; 1,978 req/s, p95 14 ms | 1.2 / 1.5 / 1.7 ms; 2,494 req/s, p95 10 ms |
| balance, latest | 2.7 / 3.1 / 3.5 ms; 1,586 req/s, p95 16 ms | 1.8 / 2.1 / 2.6 ms; 1,872 req/s, p95 13 ms |
| balance, past round | 2.6 / 3.1 / 3.5 ms; 1,675 req/s, p95 14 ms | 1.6 / 2.0 / 2.6 ms; 1,835 req/s, p95 13 ms |
| history, busiest account | 8.0 / 12.8 / 15.9 ms; 573 req/s, p95 70 ms | 5.5 / 6.8 / 7.6 ms; 871 req/s, p95 35 ms |
| history, typical account | 1.6 / 2.8 / 3.6 ms; 2,559 req/s, p95 10 ms | 1.3 / 1.9 / 2.7 ms; 2,536 req/s, p95 10 ms |
| holders, native, latest | 7.9 / 9.8 / 10.9 ms; 429 req/s, p95 50 ms | 2.0 / 2.7 / 4.1 ms; 1,211 req/s, p95 21 ms |
| holders, native, past round | 29.0 / 34.6 / 38.0 ms; 68 req/s, p95 289 ms | 6.3 / 6.9 / 7.4 ms; 417 req/s, p95 57 ms |
| holders, busiest ASA, latest | 1.9 / 2.4 / 3.0 ms; 1,807 req/s, p95 13 ms | 1.0 / 1.1 / 1.4 ms; 3,556 req/s, p95 6 ms |
| holders, busiest ARC-200, latest | 2.2 / 2.8 / 3.4 ms; 1,348 req/s, p95 19 ms | 1.1 / 1.5 / 1.7 ms; 2,795 req/s, p95 8 ms |
| token info | 0.6 / 0.8 / 0.9 ms; 7,136 req/s, p95 4 ms | 0.4 / 0.5 / 0.5 ms; 7,610 req/s, p95 4 ms |

