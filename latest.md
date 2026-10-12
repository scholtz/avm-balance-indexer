# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-12 at commit `eb41c109`: the 10,000 newest rounds of algorand-mainnet (chain rounds 65,899,580 to 65,909,579) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 81 rounds/s | 244 rounds/s |
| Effects written | 872,922 (7,103/s) | 872,922 (21,263/s) |
| Time for the range | 122.9 s | 41.1 s |
| Stored size | 137.7 MiB | 574.3 MiB |
| Per round | 14.11 KiB | 58.81 KiB |
| Per effect | 163 bytes | 678 bytes |
| If all 66 M MainNet rounds were like these | 0.9 TiB | 3.6 TiB |

Block download is the same for both and not part of the table: 205 rounds/s from the public archival nodes (232,693 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 70.6 MiB, effects_by_token_bucket 56.6 MiB, effect_buckets_by_account_token 4.1 MiB, holdings_by_token 2.0 MiB, accounts_touched_by_token 2.0 MiB
- PostgreSQL 17.11: effects_by_account_token 312.8 MiB, effects_by_token_bucket 233.6 MiB, holdings_by_token 9.0 MiB, accounts_touched_by_token 9.0 MiB, holdings_by_account 8.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 1 ARC-200 tokens, 4,528 ASAs. 2,826 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 594,578 | 5,305 | 14,190 |
| asa:127746157 |  | 53,043 | 0 | 1 |
| asa:849191641 |  | 46,806 | 2,538 | 9,261 |
| asa:31566704 |  | 41,782 | 667 | 3,338 |
| asa:127745593 |  | 30,884 | 0 | 1 |
| asa:127746786 |  | 18,809 | 0 | 1 |
| asa:3145862805 |  | 11,963 | 1,538 | 1,539 |
| asa:1284444444 |  | 6,644 | 22 | 35 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:452399768 |  | 2,892 | 74 | 101 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.4 / 2.8 ms; 3,694 req/s, p95 8 ms | 0.8 / 0.8 / 1.4 ms; 4,237 req/s, p95 6 ms |
| account holdings | 2.1 / 3.1 / 4.6 ms; 1,436 req/s, p95 21 ms | 1.8 / 2.4 / 3.2 ms; 1,722 req/s, p95 14 ms |
| balance, latest | 3.2 / 5.5 / 7.6 ms; 1,181 req/s, p95 24 ms | 2.8 / 3.4 / 3.7 ms; 1,144 req/s, p95 24 ms |
| balance, past round | 2.6 / 3.1 / 4.7 ms; 1,855 req/s, p95 13 ms | 2.3 / 2.8 / 3.6 ms; 1,332 req/s, p95 18 ms |
| history, busiest account | 4.0 / 11.2 / 13.5 ms; 712 req/s, p95 42 ms | 3.8 / 6.0 / 6.9 ms; 745 req/s, p95 32 ms |
| history, typical account | 1.9 / 2.1 / 2.8 ms; 3,191 req/s, p95 7 ms | 1.9 / 2.6 / 3.3 ms; 1,629 req/s, p95 17 ms |
| holders, native, latest | 13.3 / 14.5 / 15.7 ms; 268 req/s, p95 85 ms | 2.8 / 3.8 / 5.0 ms; 893 req/s, p95 26 ms |
| holders, native, past round | 35.9 / 41.9 / 45.1 ms; 60 req/s, p95 322 ms | 8.4 / 9.2 / 12.2 ms; 241 req/s, p95 96 ms |
| holders, busiest ASA, latest | 2.0 / 2.6 / 4.1 ms; 1,787 req/s, p95 15 ms | 1.5 / 1.7 / 1.7 ms; 2,066 req/s, p95 12 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.3 / 3.4 ms; 1,858 req/s, p95 13 ms | 1.5 / 1.6 / 1.8 ms; 2,053 req/s, p95 13 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 7,828 req/s, p95 3 ms | 0.6 / 0.6 / 0.7 ms; 5,625 req/s, p95 5 ms |

## voi

Measured on 2026-10-12 at commit `eb41c109`: the 100,000 newest rounds of voi (chain rounds 23,397,769 to 23,497,768) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 6,322 rounds/s | 10,596 rounds/s |
| Effects written | 16,085 (1,017/s) | 16,085 (1,704/s) |
| Time for the range | 15.8 s | 9.4 s |
| Stored size | 2.2 MiB | 13.5 MiB |
| Per round | 0.02 KiB | 0.14 KiB |
| Per effect | 140 bytes | 873 bytes |

Block download is the same for both and not part of the table: 261 rounds/s from the public archival nodes (6,719 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.2 MiB, effects_by_token_bucket 0.8 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 8.6 MiB, effects_by_token_bucket 4.3 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 26 ARC-200 tokens, 15 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 12,880 | 42 | 73 |
| asa:586896 |  | 1,062 | 1 | 2 |
| arc200:390001 |  | 572 | 7 | 25 |
| arc200:420069 |  | 250 | 20 | 28 |
| arc200:395614 |  | 204 | 4 | 14 |
| asa:302190 |  | 115 | 4 | 9 |
| arc200:300279 |  | 114 | 21 | 25 |
| arc200:404004 |  | 90 | 17 | 22 |
| arc200:47138068 |  | 81 | 6 | 11 |
| arc200:411311 |  | 74 | 16 | 17 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.2 / 1.5 ms; 3,917 req/s, p95 7 ms | 0.8 / 0.8 / 0.9 ms; 4,008 req/s, p95 6 ms |
| account holdings | 1.9 / 2.7 / 3.6 ms; 1,928 req/s, p95 14 ms | 1.8 / 2.2 / 2.9 ms; 1,632 req/s, p95 16 ms |
| balance, latest | 2.9 / 3.9 / 5.2 ms; 1,527 req/s, p95 17 ms | 2.6 / 3.2 / 3.7 ms; 1,227 req/s, p95 21 ms |
| balance, past round | 2.7 / 3.1 / 4.6 ms; 1,702 req/s, p95 15 ms | 2.2 / 2.8 / 3.2 ms; 1,402 req/s, p95 18 ms |
| history, busiest account | 6.1 / 8.5 / 9.7 ms; 755 req/s, p95 38 ms | 5.5 / 7.7 / 8.6 ms; 797 req/s, p95 30 ms |
| history, typical account | 1.9 / 3.1 / 4.0 ms; 2,120 req/s, p95 13 ms | 1.8 / 3.2 / 4.5 ms; 1,594 req/s, p95 16 ms |
| holders, native, latest | 3.4 / 4.7 / 5.3 ms; 837 req/s, p95 30 ms | 2.3 / 3.1 / 3.7 ms; 1,407 req/s, p95 16 ms |
| holders, native, past round | 8.7 / 10.6 / 12.0 ms; 194 req/s, p95 106 ms | 7.0 / 7.6 / 8.2 ms; 290 req/s, p95 83 ms |
| holders, busiest ASA, latest | 1.9 / 2.3 / 2.5 ms; 1,839 req/s, p95 14 ms | 1.5 / 1.7 / 1.7 ms; 2,243 req/s, p95 10 ms |
| holders, busiest ARC-200, latest | 2.3 / 2.8 / 3.4 ms; 1,321 req/s, p95 18 ms | 1.7 / 1.8 / 2.2 ms; 1,918 req/s, p95 12 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 7,769 req/s, p95 3 ms | 0.5 / 0.6 / 0.6 ms; 6,205 req/s, p95 4 ms |

