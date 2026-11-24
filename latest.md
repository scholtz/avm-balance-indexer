# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-24 at commit `ee61ce36`: the 10,000 newest rounds of algorand-mainnet (chain rounds 67,248,099 to 67,258,098) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 127 rounds/s | 274 rounds/s |
| Effects written | 804,068 (10,188/s) | 804,068 (22,054/s) |
| Time for the range | 78.9 s | 36.5 s |
| Stored size | 138.8 MiB | 543.4 MiB |
| Per round | 14.22 KiB | 55.65 KiB |
| Per effect | 177 bytes | 691 bytes |
| If all 66 M MainNet rounds were like these | 0.9 TiB | 3.4 TiB |

Block download is the same for both and not part of the table: 304 rounds/s from the public archival nodes (213,570 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 69.5 MiB, effects_by_token_bucket 54.0 MiB, effect_buckets_by_account_token 5.8 MiB, holdings_by_token 2.9 MiB, accounts_touched_by_token 2.9 MiB
- PostgreSQL 17.12: effects_by_account_token 287.7 MiB, effects_by_token_bucket 216.5 MiB, holdings_by_token 12.3 MiB, accounts_touched_by_token 12.2 MiB, holdings_by_account 11.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 8,848 ASAs. 5,240 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 537,884 | 6,977 | 18,552 |
| asa:849191641 |  | 66,358 | 4,067 | 11,599 |
| asa:31566704 |  | 34,550 | 290 | 5,448 |
| asa:127746157 |  | 34,119 | 0 | 1 |
| asa:3145862805 |  | 24,399 | 3,332 | 3,333 |
| asa:127745593 |  | 19,904 | 0 | 1 |
| asa:127746786 |  | 10,860 | 0 | 1 |
| asa:312769 |  | 6,276 | 65 | 94 |
| asa:1284444444 |  | 5,030 | 29 | 46 |
| asa:3737581431 |  | 4,800 | 24 | 24 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.8 / 2.9 ms; 3,259 req/s, p95 10 ms | 0.6 / 0.7 / 0.8 ms; 5,516 req/s, p95 5 ms |
| account holdings | 1.5 / 3.3 / 5.3 ms; 2,443 req/s, p95 12 ms | 1.2 / 1.7 / 2.0 ms; 2,469 req/s, p95 10 ms |
| balance, latest | 2.4 / 3.3 / 4.8 ms; 1,743 req/s, p95 15 ms | 1.8 / 2.3 / 2.8 ms; 1,734 req/s, p95 14 ms |
| balance, past round | 2.1 / 2.6 / 3.5 ms; 2,015 req/s, p95 12 ms | 1.5 / 2.4 / 3.9 ms; 1,821 req/s, p95 13 ms |
| history, busiest account | 6.0 / 8.7 / 10.7 ms; 538 req/s, p95 69 ms | 4.7 / 6.0 / 6.7 ms; 864 req/s, p95 34 ms |
| history, typical account | 1.3 / 1.5 / 1.9 ms; 3,360 req/s, p95 9 ms | 1.5 / 1.9 / 2.5 ms; 2,595 req/s, p95 9 ms |
| holders, native, latest | 8.1 / 9.9 / 11.8 ms; 446 req/s, p95 48 ms | 2.0 / 2.7 / 3.3 ms; 1,500 req/s, p95 18 ms |
| holders, native, past round | 24.0 / 31.7 / 36.8 ms; 93 req/s, p95 211 ms | 6.1 / 6.7 / 7.9 ms; 384 req/s, p95 64 ms |
| holders, busiest ASA, latest | 9.7 / 10.8 / 14.7 ms; 390 req/s, p95 53 ms | 1.6 / 2.0 / 2.4 ms; 1,609 req/s, p95 14 ms |
| token info | 0.5 / 0.6 / 1.0 ms; 7,575 req/s, p95 4 ms | 0.4 / 0.6 / 0.6 ms; 7,782 req/s, p95 4 ms |

## voi

Measured on 2026-11-24 at commit `ee61ce36`: the 100,000 newest rounds of voi (chain rounds 24,737,732 to 24,837,731) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 1,263 rounds/s | 6,879 rounds/s |
| Effects written | 143,964 (1,818/s) | 143,964 (9,903/s) |
| Time for the range | 79.2 s | 14.5 s |
| Stored size | 9.0 MiB | 79.7 MiB |
| Per round | 0.09 KiB | 0.82 KiB |
| Per effect | 66 bytes | 579 bytes |

Block download is the same for both and not part of the table: 47 rounds/s from the public archival nodes (12,190 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 5.4 MiB, effects_by_token_bucket 3.4 MiB, effect_buckets_by_account_token 0.1 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 45.2 MiB, effects_by_token_bucket 33.6 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 45 ARC-200 tokens, 9 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 127,155 | 124 | 170 |
| arc200:390001 |  | 5,176 | 16 | 38 |
| arc200:395614 |  | 2,152 | 13 | 21 |
| arc200:47138068 |  | 1,742 | 16 | 22 |
| arc200:300279 |  | 1,590 | 28 | 30 |
| arc200:410811 |  | 768 | 13 | 15 |
| arc200:412682 |  | 662 | 12 | 14 |
| arc200:420069 |  | 606 | 18 | 30 |
| arc200:410111 |  | 514 | 16 | 21 |
| arc200:40153155 |  | 472 | 8 | 15 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.8 / 1.0 / 1.1 ms; 3,955 req/s, p95 8 ms | 0.5 / 0.6 / 0.8 ms; 5,517 req/s, p95 5 ms |
| account holdings | 1.3 / 1.6 / 2.3 ms; 2,378 req/s, p95 12 ms | 1.1 / 1.5 / 2.1 ms; 2,619 req/s, p95 10 ms |
| balance, latest | 2.2 / 2.5 / 2.9 ms; 1,856 req/s, p95 13 ms | 1.8 / 2.4 / 2.8 ms; 1,861 req/s, p95 13 ms |
| balance, past round | 2.2 / 2.5 / 3.0 ms; 1,953 req/s, p95 12 ms | 1.5 / 1.9 / 2.2 ms; 1,889 req/s, p95 12 ms |
| history, busiest account | 6.7 / 10.1 / 14.6 ms; 504 req/s, p95 64 ms | 4.9 / 6.0 / 6.8 ms; 752 req/s, p95 40 ms |
| history, typical account | 1.5 / 2.6 / 3.0 ms; 1,973 req/s, p95 15 ms | 1.4 / 2.2 / 2.6 ms; 1,682 req/s, p95 14 ms |
| holders, native, latest | 3.3 / 4.5 / 6.7 ms; 794 req/s, p95 31 ms | 1.8 / 2.5 / 4.1 ms; 1,351 req/s, p95 19 ms |
| holders, native, past round | 12.3 / 14.9 / 18.6 ms; 134 req/s, p95 145 ms | 6.2 / 8.5 / 12.4 ms; 378 req/s, p95 65 ms |
| holders, busiest ASA, latest | 1.6 / 1.8 / 2.6 ms; 2,045 req/s, p95 12 ms | 0.9 / 1.0 / 1.4 ms; 3,365 req/s, p95 7 ms |
| holders, busiest ARC-200, latest | 1.7 / 2.3 / 2.7 ms; 1,547 req/s, p95 18 ms | 1.0 / 1.3 / 1.5 ms; 2,933 req/s, p95 8 ms |
| token info | 0.5 / 0.6 / 0.7 ms; 8,156 req/s, p95 4 ms | 0.3 / 0.4 / 0.5 ms; 8,053 req/s, p95 4 ms |

