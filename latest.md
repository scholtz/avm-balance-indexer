# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-05 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,650,341 to 66,660,340) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 104 rounds/s | 272 rounds/s |
| Effects written | 481,994 (5,029/s) | 481,994 (13,108/s) |
| Time for the range | 95.8 s | 36.8 s |
| Stored size | 88.1 MiB | 338.3 MiB |
| Per round | 9.02 KiB | 34.64 KiB |
| Per effect | 184 bytes | 706 bytes |
| If all 66 M MainNet rounds were like these | 0.6 TiB | 2.1 TiB |

Block download is the same for both and not part of the table: 332 rounds/s from the public archival nodes (138,273 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 43.8 MiB, effects_by_token_bucket 32.9 MiB, effect_buckets_by_account_token 4.4 MiB, holdings_by_token 2.1 MiB, accounts_touched_by_token 2.1 MiB
- PostgreSQL 17.11: effects_by_account_token 176.1 MiB, effects_by_token_bucket 130.9 MiB, accounts_touched_by_token 9.7 MiB, holdings_by_token 9.7 MiB, holdings_by_account 8.8 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 3 ARC-200 tokens, 7,417 ASAs. 3,493 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 333,476 | 4,853 | 17,760 |
| asa:31566704 |  | 60,071 | 406 | 4,641 |
| asa:849191641 |  | 51,631 | 2,984 | 9,700 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,546 | 16 | 23 |
| asa:2543151056 |  | 1,574 | 401 | 402 |
| asa:452399768 |  | 1,126 | 64 | 82 |
| asa:2726252423 |  | 922 | 28 | 43 |
| asa:2699078351 |  | 753 | 201 | 205 |
| asa:971384592 |  | 610 | 28 | 38 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.2 / 2.6 / 5.2 ms; 2,484 req/s, p95 12 ms | 0.7 / 0.9 / 1.5 ms; 3,562 req/s, p95 7 ms |
| account holdings | 2.1 / 4.0 / 5.5 ms; 1,855 req/s, p95 16 ms | 1.9 / 2.8 / 3.7 ms; 1,636 req/s, p95 15 ms |
| balance, latest | 2.9 / 4.1 / 6.6 ms; 1,260 req/s, p95 24 ms | 2.8 / 3.6 / 4.6 ms; 1,124 req/s, p95 23 ms |
| balance, past round | 2.7 / 3.6 / 5.4 ms; 1,684 req/s, p95 16 ms | 2.3 / 3.3 / 4.0 ms; 1,308 req/s, p95 19 ms |
| history, busiest account | 6.1 / 9.6 / 14.1 ms; 678 req/s, p95 43 ms | 5.5 / 10.0 / 14.5 ms; 800 req/s, p95 29 ms |
| history, typical account | 1.7 / 2.0 / 2.6 ms; 2,804 req/s, p95 10 ms | 1.9 / 2.5 / 2.9 ms; 1,620 req/s, p95 16 ms |
| holders, native, latest | 12.6 / 15.9 / 18.3 ms; 280 req/s, p95 83 ms | 2.8 / 3.8 / 4.5 ms; 944 req/s, p95 24 ms |
| holders, native, past round | 29.1 / 32.5 / 35.9 ms; 65 req/s, p95 297 ms | 8.5 / 9.3 / 9.8 ms; 243 req/s, p95 100 ms |
| holders, busiest ASA, latest | 54.7 / 59.5 / 64.7 ms; 90 req/s, p95 217 ms | 2.8 / 3.3 / 3.5 ms; 814 req/s, p95 27 ms |
| holders, busiest ARC-200, latest | 2.1 / 2.5 / 2.6 ms; 1,718 req/s, p95 14 ms | 1.6 / 1.7 / 2.1 ms; 2,039 req/s, p95 13 ms |
| token info | 0.7 / 0.9 / 0.9 ms; 6,712 req/s, p95 4 ms | 0.6 / 0.7 / 1.3 ms; 5,069 req/s, p95 6 ms |

## voi

Measured on 2026-11-05 at commit `9e2e5bda`: the 100,000 newest rounds of voi (chain rounds 24,142,240 to 24,242,239) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 2,366 rounds/s | 5,430 rounds/s |
| Effects written | 74,938 (1,773/s) | 74,938 (4,069/s) |
| Time for the range | 42.3 s | 18.4 s |
| Stored size | 8.2 MiB | 48.8 MiB |
| Per round | 0.08 KiB | 0.50 KiB |
| Per effect | 114 bytes | 679 bytes |

Block download is the same for both and not part of the table: 28 rounds/s from the public archival nodes (17,047 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 4.7 MiB, effects_by_token_bucket 3.2 MiB, effect_buckets_by_account_token 0.1 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 28.9 MiB, effects_by_token_bucket 19.0 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 75 ARC-200 tokens, 14 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 55,300 | 157 | 235 |
| arc200:390001 |  | 6,724 | 25 | 36 |
| arc200:395614 |  | 1,536 | 7 | 17 |
| arc200:47138068 |  | 1,364 | 10 | 19 |
| arc200:410111 |  | 1,264 | 19 | 26 |
| arc200:300279 |  | 1,118 | 29 | 34 |
| arc200:420069 |  | 852 | 15 | 27 |
| arc200:412682 |  | 752 | 13 | 19 |
| arc200:40153155 |  | 668 | 9 | 23 |
| arc200:410811 |  | 640 | 12 | 15 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.3 / 2.4 ms; 3,117 req/s, p95 10 ms | 0.8 / 0.9 / 1.9 ms; 3,189 req/s, p95 8 ms |
| account holdings | 1.8 / 2.6 / 3.3 ms; 1,909 req/s, p95 15 ms | 1.9 / 2.5 / 3.3 ms; 1,630 req/s, p95 15 ms |
| balance, latest | 2.9 / 3.5 / 3.8 ms; 1,602 req/s, p95 16 ms | 2.8 / 3.5 / 4.1 ms; 1,144 req/s, p95 22 ms |
| balance, past round | 2.8 / 3.7 / 5.0 ms; 1,172 req/s, p95 23 ms | 2.4 / 3.0 / 3.6 ms; 1,270 req/s, p95 19 ms |
| history, busiest account | 6.6 / 8.9 / 10.1 ms; 697 req/s, p95 40 ms | 5.4 / 8.3 / 8.7 ms; 754 req/s, p95 32 ms |
| history, typical account | 1.8 / 3.2 / 3.6 ms; 2,107 req/s, p95 14 ms | 1.9 / 3.1 / 3.9 ms; 1,442 req/s, p95 18 ms |
| holders, native, latest | 5.7 / 7.4 / 9.8 ms; 478 req/s, p95 49 ms | 3.0 / 3.8 / 4.3 ms; 863 req/s, p95 27 ms |
| holders, native, past round | 28.5 / 34.7 / 43.6 ms; 92 req/s, p95 208 ms | 9.1 / 11.8 / 12.6 ms; 242 req/s, p95 98 ms |
| holders, busiest ASA, latest | 2.1 / 2.5 / 3.2 ms; 1,640 req/s, p95 16 ms | 1.6 / 1.8 / 2.4 ms; 2,061 req/s, p95 11 ms |
| holders, busiest ARC-200, latest | 2.6 / 3.4 / 4.0 ms; 1,061 req/s, p95 25 ms | 2.0 / 2.6 / 2.8 ms; 1,565 req/s, p95 14 ms |
| token info | 0.7 / 0.9 / 0.9 ms; 6,776 req/s, p95 4 ms | 0.6 / 0.7 / 0.8 ms; 5,394 req/s, p95 5 ms |

