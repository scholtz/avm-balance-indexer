# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-09 at commit `e3803b2e`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,776,410 to 66,786,409) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 110 rounds/s | 290 rounds/s |
| Effects written | 471,293 (5,202/s) | 471,293 (13,651/s) |
| Time for the range | 90.6 s | 34.5 s |
| Stored size | 86.8 MiB | 327.4 MiB |
| Per round | 8.89 KiB | 33.52 KiB |
| Per effect | 186 bytes | 703 bytes |
| If all 66 M MainNet rounds were like these | 0.5 TiB | 2.1 TiB |

Block download is the same for both and not part of the table: 297 rounds/s from the public archival nodes (119,728 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 43.5 MiB, effects_by_token_bucket 32.4 MiB, effect_buckets_by_account_token 4.2 MiB, holdings_by_token 2.1 MiB, accounts_touched_by_token 2.0 MiB
- PostgreSQL 17.11: effects_by_account_token 171.0 MiB, effects_by_token_bucket 127.4 MiB, holdings_by_token 9.1 MiB, accounts_touched_by_token 9.0 MiB, holdings_by_account 8.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 6 ARC-200 tokens, 6,772 ASAs. 3,293 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 320,602 | 4,476 | 14,682 |
| asa:849191641 |  | 54,788 | 3,497 | 9,835 |
| asa:31566704 |  | 39,190 | 520 | 4,417 |
| asa:3737581431 |  | 4,800 | 23 | 24 |
| asa:1284444444 |  | 4,669 | 28 | 40 |
| asa:3203964481 |  | 4,133 | 47 | 91 |
| asa:1134696561 |  | 2,142 | 28 | 47 |
| asa:452399768 |  | 1,882 | 68 | 83 |
| asa:2726252423 |  | 1,704 | 40 | 64 |
| asa:2543151056 |  | 1,578 | 402 | 403 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 1.6 / 2.7 ms; 2,691 req/s, p95 10 ms | 0.8 / 0.9 / 1.2 ms; 3,446 req/s, p95 8 ms |
| account holdings | 2.0 / 5.1 / 8.0 ms; 1,583 req/s, p95 17 ms | 1.9 / 2.3 / 3.2 ms; 1,649 req/s, p95 14 ms |
| balance, latest | 3.1 / 5.9 / 8.1 ms; 1,308 req/s, p95 19 ms | 2.8 / 3.8 / 4.8 ms; 1,192 req/s, p95 21 ms |
| balance, past round | 2.5 / 3.2 / 4.3 ms; 1,714 req/s, p95 14 ms | 2.3 / 3.1 / 4.0 ms; 1,322 req/s, p95 17 ms |
| history, busiest account | 6.0 / 9.5 / 11.4 ms; 631 req/s, p95 48 ms | 5.8 / 8.3 / 10.0 ms; 772 req/s, p95 30 ms |
| history, typical account | 1.6 / 1.8 / 2.4 ms; 2,829 req/s, p95 9 ms | 1.9 / 2.1 / 2.6 ms; 1,651 req/s, p95 15 ms |
| holders, native, latest | 15.5 / 18.8 / 22.8 ms; 258 req/s, p95 76 ms | 2.9 / 3.8 / 5.2 ms; 938 req/s, p95 24 ms |
| holders, native, past round | 33.9 / 43.7 / 49.6 ms; 61 req/s, p95 310 ms | 8.8 / 9.5 / 11.8 ms; 247 req/s, p95 94 ms |
| holders, busiest ASA, latest | 11.8 / 13.9 / 16.6 ms; 316 req/s, p95 64 ms | 2.6 / 3.3 / 4.2 ms; 982 req/s, p95 22 ms |
| holders, busiest ARC-200, latest | 2.1 / 3.0 / 3.6 ms; 1,633 req/s, p95 16 ms | 1.6 / 1.7 / 2.1 ms; 2,131 req/s, p95 10 ms |
| token info | 0.6 / 0.7 / 0.8 ms; 5,620 req/s, p95 5 ms | 0.6 / 0.7 / 1.3 ms; 5,208 req/s, p95 5 ms |

## voi

Measured on 2026-11-09 at commit `e3803b2e`: the 100,000 newest rounds of voi (chain rounds 24,267,581 to 24,367,580) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 5,512 rounds/s | 9,301 rounds/s |
| Effects written | 24,089 (1,328/s) | 24,089 (2,241/s) |
| Time for the range | 18.1 s | 10.8 s |
| Stored size | 2.9 MiB | 17.7 MiB |
| Per round | 0.03 KiB | 0.18 KiB |
| Per effect | 124 bytes | 768 bytes |

Block download is the same for both and not part of the table: 65 rounds/s from the public archival nodes (7,507 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.6 MiB, effects_by_token_bucket 1.2 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 10.9 MiB, effects_by_token_bucket 6.3 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 24 ARC-200 tokens, 6 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 18,484 | 57 | 92 |
| arc200:390001 |  | 1,624 | 12 | 23 |
| arc200:40153155 |  | 892 | 4 | 12 |
| asa:586896 |  | 640 | 1 | 2 |
| arc200:420024 |  | 446 | 4 | 7 |
| asa:40152679 |  | 432 | 1 | 2 |
| arc200:395614 |  | 340 | 3 | 13 |
| arc200:420069 |  | 216 | 17 | 21 |
| arc200:300279 |  | 154 | 19 | 25 |
| arc200:47138068 |  | 126 | 4 | 9 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 1.1 / 1.7 ms; 3,278 req/s, p95 9 ms | 0.7 / 0.8 / 1.8 ms; 3,404 req/s, p95 8 ms |
| account holdings | 1.7 / 2.1 / 3.1 ms; 1,495 req/s, p95 21 ms | 1.9 / 2.5 / 3.2 ms; 1,640 req/s, p95 14 ms |
| balance, latest | 2.7 / 3.2 / 4.9 ms; 1,536 req/s, p95 16 ms | 2.8 / 3.8 / 4.4 ms; 1,228 req/s, p95 19 ms |
| balance, past round | 2.6 / 3.2 / 4.0 ms; 1,579 req/s, p95 16 ms | 2.4 / 3.1 / 3.6 ms; 1,412 req/s, p95 16 ms |
| history, busiest account | 6.4 / 9.7 / 14.9 ms; 736 req/s, p95 37 ms | 5.6 / 8.2 / 10.3 ms; 799 req/s, p95 29 ms |
| history, typical account | 1.7 / 3.1 / 3.6 ms; 1,970 req/s, p95 14 ms | 1.8 / 3.4 / 4.6 ms; 1,563 req/s, p95 16 ms |
| holders, native, latest | 3.6 / 5.2 / 7.9 ms; 777 req/s, p95 31 ms | 2.5 / 3.1 / 3.5 ms; 1,233 req/s, p95 18 ms |
| holders, native, past round | 10.1 / 14.2 / 15.9 ms; 180 req/s, p95 118 ms | 7.5 / 8.4 / 9.7 ms; 268 req/s, p95 88 ms |
| holders, busiest ASA, latest | 2.0 / 2.4 / 3.2 ms; 1,605 req/s, p95 16 ms | 1.7 / 1.8 / 2.2 ms; 2,240 req/s, p95 9 ms |
| holders, busiest ARC-200, latest | 2.5 / 4.1 / 6.8 ms; 1,257 req/s, p95 20 ms | 1.9 / 2.6 / 3.7 ms; 1,817 req/s, p95 12 ms |
| token info | 0.6 / 0.7 / 0.9 ms; 6,991 req/s, p95 4 ms | 0.6 / 0.7 / 0.7 ms; 5,906 req/s, p95 4 ms |

