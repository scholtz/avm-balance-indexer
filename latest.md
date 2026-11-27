# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-27 at commit `ee61ce36`: the 10,000 newest rounds of algorand-mainnet (chain rounds 67,338,827 to 67,348,826) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 138 rounds/s | 277 rounds/s |
| Effects written | 590,848 (8,175/s) | 590,848 (16,395/s) |
| Time for the range | 72.3 s | 36.0 s |
| Stored size | 100.3 MiB | 398.2 MiB |
| Per round | 10.27 KiB | 40.78 KiB |
| Per effect | 174 bytes | 692 bytes |
| If all 66 M MainNet rounds were like these | 0.6 TiB | 2.5 TiB |

Block download is the same for both and not part of the table: 302 rounds/s from the public archival nodes (168,620 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 49.5 MiB, effects_by_token_bucket 39.3 MiB, effect_buckets_by_account_token 4.2 MiB, holdings_by_token 2.2 MiB, accounts_touched_by_token 2.1 MiB
- PostgreSQL 17.12: effects_by_account_token 211.6 MiB, effects_by_token_bucket 157.9 MiB, holdings_by_token 8.6 MiB, accounts_touched_by_token 8.5 MiB, holdings_by_account 7.9 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 5 ARC-200 tokens, 8,553 ASAs. 6,497 of those ASAs were created inside the range, 1 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 420,426 | 5,690 | 12,820 |
| asa:127746157 |  | 34,007 | 0 | 1 |
| asa:849191641 |  | 23,380 | 1,474 | 5,905 |
| asa:127745593 |  | 20,914 | 0 | 1 |
| asa:3145862805 |  | 20,562 | 3,037 | 3,038 |
| asa:31566704 |  | 20,384 | 263 | 4,822 |
| asa:127746786 |  | 9,498 | 0 | 1 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,252 | 13 | 20 |
| asa:2543151056 |  | 1,632 | 416 | 417 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.4 / 2.8 / 4.9 ms; 3,411 req/s, p95 10 ms | 0.9 / 1.2 / 3.1 ms; 3,739 req/s, p95 8 ms |
| account holdings | 2.1 / 3.3 / 3.8 ms; 1,383 req/s, p95 21 ms | 1.7 / 2.3 / 3.3 ms; 1,764 req/s, p95 15 ms |
| balance, latest | 3.2 / 6.6 / 10.0 ms; 1,310 req/s, p95 20 ms | 2.5 / 3.1 / 3.8 ms; 1,296 req/s, p95 19 ms |
| balance, past round | 2.8 / 3.5 / 4.9 ms; 1,510 req/s, p95 16 ms | 2.1 / 2.7 / 3.3 ms; 1,412 req/s, p95 17 ms |
| history, busiest account | 7.4 / 11.5 / 15.2 ms; 705 req/s, p95 41 ms | 6.4 / 8.9 / 10.1 ms; 462 req/s, p95 61 ms |
| history, typical account | 1.5 / 1.8 / 2.4 ms; 3,080 req/s, p95 9 ms | 1.6 / 2.2 / 2.6 ms; 2,021 req/s, p95 12 ms |
| holders, native, latest | 8.3 / 12.6 / 16.1 ms; 395 req/s, p95 59 ms | 2.8 / 3.9 / 6.0 ms; 1,017 req/s, p95 23 ms |
| holders, native, past round | 34.0 / 40.5 / 42.8 ms; 74 req/s, p95 256 ms | 7.6 / 8.4 / 10.6 ms; 310 req/s, p95 78 ms |
| holders, busiest ASA, latest | 1.7 / 2.3 / 3.4 ms; 2,170 req/s, p95 13 ms | 1.3 / 1.5 / 1.6 ms; 2,684 req/s, p95 8 ms |
| holders, busiest ARC-200, latest | 1.7 / 2.0 / 2.6 ms; 1,813 req/s, p95 16 ms | 1.3 / 1.6 / 1.8 ms; 2,626 req/s, p95 9 ms |
| token info | 0.7 / 1.1 / 1.7 ms; 8,197 req/s, p95 4 ms | 0.6 / 0.7 / 0.9 ms; 5,206 req/s, p95 5 ms |

## voi

Measured on 2026-11-27 at commit `ee61ce36`: the 100,000 newest rounds of voi (chain rounds 24,829,251 to 24,929,250) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 2,015 rounds/s | 7,241 rounds/s |
| Effects written | 91,392 (1,841/s) | 91,392 (6,617/s) |
| Time for the range | 49.6 s | 13.8 s |
| Stored size | 6.9 MiB | 53.1 MiB |
| Per round | 0.07 KiB | 0.54 KiB |
| Per effect | 78 bytes | 607 bytes |

Block download is the same for both and not part of the table: 55 rounds/s from the public archival nodes (11,914 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 4.0 MiB, effects_by_token_bucket 2.6 MiB, effect_buckets_by_account_token 0.1 MiB, holdings_by_token 0.0 MiB, holdings_by_account 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 30.4 MiB, effects_by_token_bucket 21.8 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 45 ARC-200 tokens, 7 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 80,638 | 164 | 304 |
| arc200:390001 |  | 3,294 | 18 | 37 |
| arc200:395614 |  | 1,174 | 12 | 16 |
| arc200:420069 |  | 1,110 | 11 | 23 |
| arc200:47138068 |  | 956 | 11 | 19 |
| arc200:300279 |  | 780 | 23 | 34 |
| arc200:40153155 |  | 446 | 18 | 22 |
| arc200:410419 |  | 382 | 3 | 12 |
| arc200:410811 |  | 318 | 11 | 15 |
| arc200:412682 |  | 284 | 7 | 13 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.1 / 1.6 / 1.9 ms; 4,095 req/s, p95 8 ms | 0.8 / 1.0 / 1.2 ms; 3,615 req/s, p95 9 ms |
| account holdings | 1.9 / 2.1 / 3.1 ms; 2,018 req/s, p95 14 ms | 1.8 / 2.2 / 3.0 ms; 1,807 req/s, p95 14 ms |
| balance, latest | 2.8 / 3.4 / 4.1 ms; 1,657 req/s, p95 14 ms | 2.6 / 3.0 / 3.5 ms; 1,315 req/s, p95 18 ms |
| balance, past round | 2.7 / 3.1 / 3.7 ms; 1,552 req/s, p95 17 ms | 2.2 / 2.9 / 3.5 ms; 1,349 req/s, p95 18 ms |
| history, busiest account | 7.7 / 10.0 / 11.6 ms; 502 req/s, p95 73 ms | 6.6 / 9.5 / 11.2 ms; 469 req/s, p95 59 ms |
| history, typical account | 1.6 / 3.3 / 6.8 ms; 2,387 req/s, p95 12 ms | 1.7 / 2.9 / 3.5 ms; 1,867 req/s, p95 13 ms |
| holders, native, latest | 6.9 / 8.5 / 9.9 ms; 492 req/s, p95 44 ms | 2.6 / 3.6 / 4.5 ms; 1,052 req/s, p95 21 ms |
| holders, native, past round | 14.0 / 17.8 / 24.1 ms; 106 req/s, p95 183 ms | 8.5 / 9.4 / 10.0 ms; 275 req/s, p95 86 ms |
| holders, busiest ASA, latest | 1.8 / 2.1 / 2.3 ms; 2,020 req/s, p95 12 ms | 1.3 / 1.5 / 1.6 ms; 2,570 req/s, p95 9 ms |
| holders, busiest ARC-200, latest | 2.1 / 2.5 / 3.5 ms; 1,498 req/s, p95 18 ms | 1.5 / 2.0 / 2.2 ms; 2,009 req/s, p95 11 ms |
| token info | 0.6 / 0.8 / 0.8 ms; 7,765 req/s, p95 4 ms | 0.5 / 0.6 / 0.6 ms; 6,253 req/s, p95 4 ms |

