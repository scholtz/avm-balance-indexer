# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-30 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,461,709 to 66,471,708) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 220 rounds/s | 268 rounds/s |
| Effects written | 541,260 (11,905/s) | 541,260 (14,520/s) |
| Time for the range | 45.5 s | 37.3 s |
| Stored size | 90.6 MiB | 365.8 MiB |
| Per round | 9.27 KiB | 37.45 KiB |
| Per effect | 172 bytes | 694 bytes |
| If all 66 M MainNet rounds were like these | 0.6 TiB | 2.3 TiB |

Block download is the same for both and not part of the table: 147 rounds/s from the public archival nodes (154,022 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 46.4 MiB, effects_by_token_bucket 34.1 MiB, effect_buckets_by_account_token 3.7 MiB, holdings_by_token 1.9 MiB, accounts_touched_by_token 1.8 MiB
- PostgreSQL 17.11: effects_by_account_token 195.1 MiB, effects_by_token_bucket 145.0 MiB, holdings_by_token 7.9 MiB, accounts_touched_by_token 7.9 MiB, holdings_by_account 7.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 2 ARC-200 tokens, 6,477 ASAs. 3,669 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 404,492 | 4,562 | 14,473 |
| asa:31566704 |  | 63,317 | 524 | 4,313 |
| asa:849191641 |  | 15,591 | 984 | 4,187 |
| asa:3145862805 |  | 13,507 | 1,823 | 1,824 |
| asa:1284444444 |  | 4,845 | 20 | 38 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:2543151056 |  | 1,510 | 385 | 386 |
| asa:452399768 |  | 1,470 | 61 | 87 |
| asa:2726252423 |  | 1,029 | 33 | 60 |
| asa:3203964481 |  | 941 | 41 | 62 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.7 / 1.8 / 3.0 ms; 7,764 req/s, p95 4 ms | 0.6 / 0.7 / 0.7 ms; 4,657 req/s, p95 6 ms |
| account holdings | 1.2 / 2.5 / 4.2 ms; 2,729 req/s, p95 12 ms | 1.4 / 1.9 / 2.7 ms; 2,196 req/s, p95 12 ms |
| balance, latest | 1.7 / 3.4 / 5.0 ms; 2,699 req/s, p95 11 ms | 2.1 / 2.6 / 3.7 ms; 1,622 req/s, p95 16 ms |
| balance, past round | 1.5 / 2.6 / 3.1 ms; 3,811 req/s, p95 7 ms | 1.7 / 2.2 / 2.6 ms; 1,716 req/s, p95 14 ms |
| history, busiest account | 3.5 / 4.9 / 8.8 ms; 683 req/s, p95 50 ms | 4.9 / 6.3 / 7.0 ms; 795 req/s, p95 36 ms |
| history, typical account | 0.9 / 1.3 / 2.1 ms; 6,597 req/s, p95 4 ms | 1.4 / 1.7 / 2.3 ms; 2,216 req/s, p95 12 ms |
| holders, native, latest | 6.7 / 9.5 / 11.4 ms; 585 req/s, p95 36 ms | 2.2 / 3.1 / 4.4 ms; 1,210 req/s, p95 19 ms |
| holders, native, past round | 14.3 / 16.2 / 18.1 ms; 144 req/s, p95 139 ms | 7.4 / 8.3 / 10.4 ms; 293 req/s, p95 81 ms |
| holders, busiest ASA, latest | 17.3 / 18.7 / 19.2 ms; 267 req/s, p95 74 ms | 2.1 / 2.5 / 2.7 ms; 1,125 req/s, p95 20 ms |
| holders, busiest ARC-200, latest | 0.9 / 1.2 / 1.9 ms; 3,773 req/s, p95 8 ms | 1.2 / 1.3 / 1.7 ms; 2,933 req/s, p95 8 ms |
| token info | 0.3 / 0.4 / 0.4 ms; 20,081 req/s, p95 1 ms | 0.5 / 0.5 / 0.6 ms; 6,465 req/s, p95 4 ms |

## voi

Measured on 2026-10-30 at commit `9e2e5bda`: the 100,000 newest rounds of voi (chain rounds 23,953,725 to 24,053,724) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 6,690 rounds/s | 6,659 rounds/s |
| Effects written | 37,271 (2,493/s) | 37,271 (2,482/s) |
| Time for the range | 14.9 s | 15.0 s |
| Stored size | 3.4 MiB | 24.6 MiB |
| Per round | 0.03 KiB | 0.25 KiB |
| Per effect | 95 bytes | 688 bytes |

Block download is the same for both and not part of the table: 31 rounds/s from the public archival nodes (7,860 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.9 MiB, effects_by_token_bucket 1.3 MiB, effect_buckets_by_account_token 0.1 MiB, holdings_by_token 0.0 MiB, holdings_by_account 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 14.7 MiB, effects_by_token_bucket 9.2 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 36 ARC-200 tokens, 7 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 33,391 | 151 | 198 |
| asa:586896 |  | 1,164 | 1 | 2 |
| arc200:390001 |  | 832 | 15 | 20 |
| arc200:40153155 |  | 286 | 6 | 22 |
| arc200:395614 |  | 232 | 4 | 12 |
| arc200:420069 |  | 160 | 13 | 21 |
| arc200:47138068 |  | 141 | 6 | 12 |
| arc200:300279 |  | 134 | 21 | 25 |
| asa:40152679 |  | 100 | 4 | 9 |
| arc200:404004 |  | 96 | 20 | 26 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.6 / 0.7 / 1.0 ms; 7,620 req/s, p95 4 ms | 0.5 / 0.6 / 1.3 ms; 4,703 req/s, p95 5 ms |
| account holdings | 1.0 / 1.3 / 2.0 ms; 3,758 req/s, p95 8 ms | 1.4 / 1.7 / 2.5 ms; 2,243 req/s, p95 11 ms |
| balance, latest | 1.5 / 1.9 / 3.5 ms; 3,521 req/s, p95 7 ms | 2.1 / 2.7 / 3.5 ms; 1,653 req/s, p95 15 ms |
| balance, past round | 1.5 / 1.7 / 2.3 ms; 3,199 req/s, p95 8 ms | 1.7 / 2.3 / 3.0 ms; 1,834 req/s, p95 14 ms |
| history, busiest account | 4.2 / 6.4 / 9.8 ms; 971 req/s, p95 33 ms | 4.5 / 6.6 / 7.5 ms; 614 req/s, p95 41 ms |
| history, typical account | 1.0 / 1.9 / 4.2 ms; 3,361 req/s, p95 9 ms | 1.4 / 2.3 / 3.0 ms; 2,247 req/s, p95 12 ms |
| holders, native, latest | 2.4 / 3.5 / 4.1 ms; 1,034 req/s, p95 27 ms | 2.1 / 2.7 / 3.3 ms; 1,245 req/s, p95 19 ms |
| holders, native, past round | 7.0 / 9.3 / 14.0 ms; 194 req/s, p95 113 ms | 7.4 / 8.1 / 12.6 ms; 282 req/s, p95 88 ms |
| holders, busiest ASA, latest | 0.8 / 1.0 / 1.3 ms; 4,542 req/s, p95 6 ms | 1.2 / 1.3 / 1.5 ms; 3,097 req/s, p95 8 ms |
| holders, busiest ARC-200, latest | 1.0 / 1.3 / 2.1 ms; 2,875 req/s, p95 10 ms | 1.4 / 2.0 / 2.9 ms; 2,417 req/s, p95 10 ms |
| token info | 0.3 / 0.4 / 0.4 ms; 19,608 req/s, p95 1 ms | 0.4 / 0.5 / 0.7 ms; 7,616 req/s, p95 4 ms |

