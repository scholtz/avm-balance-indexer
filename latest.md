# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-15 at commit `bfdee74f`: the 10,000 newest rounds of algorand-mainnet (chain rounds 65,990,963 to 66,000,962) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 64 rounds/s | 184 rounds/s |
| Effects written | 1,170,303 (7,526/s) | 1,170,303 (21,504/s) |
| Time for the range | 155.5 s | 54.4 s |
| Stored size | 185.3 MiB | 765.9 MiB |
| Per round | 18.98 KiB | 78.42 KiB |
| Per effect | 164 bytes | 678 bytes |
| If all 66 M MainNet rounds were like these | 1.2 TiB | 4.8 TiB |

Block download is the same for both and not part of the table: 225 rounds/s from the public archival nodes (312,375 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 96.0 MiB, effects_by_token_bucket 74.9 MiB, effect_buckets_by_account_token 5.3 MiB, holdings_by_token 2.7 MiB, accounts_touched_by_token 2.7 MiB
- PostgreSQL 17.11: effects_by_account_token 416.8 MiB, effects_by_token_bucket 312.4 MiB, holdings_by_token 11.4 MiB, accounts_touched_by_token 11.3 MiB, holdings_by_account 10.4 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 8 ARC-200 tokens, 8,380 ASAs. 6,387 of those ASAs were created inside the range, 84 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 827,952 | 8,076 | 19,555 |
| asa:31566704 |  | 84,419 | 479 | 5,553 |
| asa:127746157 |  | 53,610 | 0 | 1 |
| asa:849191641 |  | 37,177 | 1,993 | 7,367 |
| asa:127745593 |  | 29,059 | 0 | 1 |
| asa:127746786 |  | 20,884 | 0 | 1 |
| asa:3145862805 |  | 11,898 | 1,646 | 1,647 |
| asa:1284444444 |  | 6,098 | 43 | 66 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:386192725 |  | 4,205 | 34 | 117 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.4 / 2.3 ms; 3,333 req/s, p95 10 ms | 0.8 / 0.8 / 0.9 ms; 3,900 req/s, p95 6 ms |
| account holdings | 2.0 / 3.4 / 4.4 ms; 1,917 req/s, p95 15 ms | 1.9 / 2.6 / 4.3 ms; 1,631 req/s, p95 15 ms |
| balance, latest | 3.1 / 4.6 / 5.8 ms; 1,503 req/s, p95 17 ms | 3.0 / 3.8 / 5.1 ms; 1,043 req/s, p95 24 ms |
| balance, past round | 2.7 / 3.1 / 3.6 ms; 1,792 req/s, p95 13 ms | 2.3 / 3.3 / 3.8 ms; 1,351 req/s, p95 18 ms |
| history, busiest account | 6.2 / 9.8 / 12.0 ms; 722 req/s, p95 40 ms | 3.4 / 4.5 / 5.6 ms; 703 req/s, p95 33 ms |
| history, typical account | 1.9 / 2.1 / 2.4 ms; 2,663 req/s, p95 10 ms | 2.0 / 2.6 / 3.2 ms; 1,510 req/s, p95 18 ms |
| holders, native, latest | 11.8 / 14.4 / 15.3 ms; 319 req/s, p95 63 ms | 2.9 / 3.8 / 4.3 ms; 905 req/s, p95 25 ms |
| holders, native, past round | 45.6 / 53.9 / 57.9 ms; 55 req/s, p95 344 ms | 9.2 / 10.1 / 10.8 ms; 225 req/s, p95 102 ms |
| holders, busiest ASA, latest | 59.7 / 61.5 / 63.7 ms; 87 req/s, p95 214 ms | 3.1 / 3.7 / 4.2 ms; 751 req/s, p95 29 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.4 / 3.1 ms; 1,741 req/s, p95 14 ms | 1.7 / 1.8 / 2.4 ms; 1,980 req/s, p95 12 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 7,704 req/s, p95 3 ms | 0.6 / 0.7 / 0.7 ms; 5,347 req/s, p95 5 ms |

## voi

Measured on 2026-10-15 at commit `bfdee74f`: the 100,000 newest rounds of voi (chain rounds 23,487,781 to 23,587,780) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 2,621 rounds/s | 5,869 rounds/s |
| Effects written | 74,172 (1,944/s) | 74,172 (4,353/s) |
| Time for the range | 38.1 s | 17.0 s |
| Stored size | 8.5 MiB | 46.6 MiB |
| Per round | 0.09 KiB | 0.48 KiB |
| Per effect | 121 bytes | 658 bytes |

Block download is the same for both and not part of the table: 86 rounds/s from the public archival nodes (24,114 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 5.0 MiB, effects_by_token_bucket 3.4 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_account 0.0 MiB, holdings_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 26.1 MiB, effects_by_token_bucket 19.9 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 23 ARC-200 tokens, 6 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 70,636 | 151 | 180 |
| asa:586896 |  | 1,698 | 1 | 2 |
| arc200:390001 |  | 608 | 12 | 19 |
| arc200:395614 |  | 264 | 7 | 11 |
| arc200:47138068 |  | 260 | 12 | 19 |
| asa:302190 |  | 98 | 4 | 7 |
| arc200:300279 |  | 84 | 20 | 24 |
| arc200:420069 |  | 84 | 8 | 16 |
| arc200:404004 |  | 64 | 16 | 17 |
| arc200:411311 |  | 64 | 16 | 17 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.2 / 2.0 ms; 3,745 req/s, p95 8 ms | 0.8 / 0.8 / 1.0 ms; 3,757 req/s, p95 7 ms |
| account holdings | 1.8 / 2.2 / 3.6 ms; 1,970 req/s, p95 14 ms | 1.9 / 2.4 / 3.6 ms; 1,663 req/s, p95 15 ms |
| balance, latest | 2.9 / 3.3 / 3.6 ms; 1,633 req/s, p95 15 ms | 2.7 / 3.3 / 3.6 ms; 1,132 req/s, p95 24 ms |
| balance, past round | 2.8 / 3.2 / 3.8 ms; 1,846 req/s, p95 13 ms | 2.5 / 3.0 / 3.5 ms; 1,292 req/s, p95 18 ms |
| history, busiest account | 6.8 / 11.6 / 14.6 ms; 681 req/s, p95 40 ms | 6.2 / 8.7 / 9.4 ms; 614 req/s, p95 45 ms |
| history, typical account | 1.8 / 2.1 / 2.4 ms; 3,084 req/s, p95 8 ms | 1.8 / 2.4 / 2.7 ms; 1,628 req/s, p95 16 ms |
| holders, native, latest | 4.4 / 5.9 / 6.7 ms; 561 req/s, p95 47 ms | 2.9 / 3.8 / 4.1 ms; 928 req/s, p95 25 ms |
| holders, native, past round | 16.7 / 20.2 / 23.4 ms; 164 req/s, p95 115 ms | 7.9 / 8.6 / 9.3 ms; 279 req/s, p95 87 ms |
| holders, busiest ASA, latest | 2.0 / 2.3 / 2.5 ms; 1,838 req/s, p95 13 ms | 1.6 / 1.7 / 1.8 ms; 2,043 req/s, p95 11 ms |
| holders, busiest ARC-200, latest | 2.4 / 3.1 / 3.7 ms; 1,125 req/s, p95 23 ms | 1.9 / 2.2 / 2.6 ms; 1,664 req/s, p95 15 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 7,485 req/s, p95 3 ms | 0.6 / 0.7 / 0.7 ms; 6,267 req/s, p95 4 ms |

