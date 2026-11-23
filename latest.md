# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-23 at commit `ee61ce36`: the 10,000 newest rounds of algorand-mainnet (chain rounds 67,214,635 to 67,224,634) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 89 rounds/s | 228 rounds/s |
| Effects written | 775,875 (6,924/s) | 775,875 (17,656/s) |
| Time for the range | 112.0 s | 43.9 s |
| Stored size | 130.8 MiB | 521.3 MiB |
| Per round | 13.40 KiB | 53.38 KiB |
| Per effect | 173 bytes | 689 bytes |
| If all 66 M MainNet rounds were like these | 0.8 TiB | 3.3 TiB |

Block download is the same for both and not part of the table: 260 rounds/s from the public archival nodes (197,503 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 66.7 MiB, effects_by_token_bucket 51.0 MiB, effect_buckets_by_account_token 5.1 MiB, holdings_by_token 2.5 MiB, accounts_touched_by_token 2.4 MiB
- PostgreSQL 17.12: effects_by_account_token 278.6 MiB, effects_by_token_bucket 208.5 MiB, holdings_by_token 10.8 MiB, accounts_touched_by_token 10.8 MiB, holdings_by_account 9.9 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 10 ARC-200 tokens, 6,174 ASAs. 3,731 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 518,580 | 5,054 | 15,882 |
| asa:849191641 |  | 54,165 | 4,423 | 10,816 |
| asa:31566704 |  | 46,595 | 534 | 4,597 |
| asa:127746157 |  | 27,065 | 0 | 1 |
| asa:3145862805 |  | 20,018 | 2,566 | 2,567 |
| asa:127745593 |  | 16,321 | 0 | 1 |
| asa:127746786 |  | 9,001 | 0 | 1 |
| asa:3203964481 |  | 5,794 | 67 | 95 |
| asa:1284444444 |  | 5,659 | 20 | 35 |
| asa:3737581431 |  | 4,796 | 23 | 24 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.4 / 2.8 / 5.1 ms; 2,959 req/s, p95 11 ms | 0.8 / 1.0 / 1.1 ms; 3,258 req/s, p95 8 ms |
| account holdings | 2.1 / 3.6 / 5.1 ms; 1,414 req/s, p95 21 ms | 1.9 / 2.5 / 3.3 ms; 1,332 req/s, p95 19 ms |
| balance, latest | 3.3 / 6.4 / 10.2 ms; 1,116 req/s, p95 25 ms | 3.3 / 5.5 / 6.6 ms; 1,025 req/s, p95 24 ms |
| balance, past round | 2.9 / 3.8 / 4.3 ms; 1,273 req/s, p95 19 ms | 2.4 / 3.3 / 4.0 ms; 1,282 req/s, p95 17 ms |
| history, busiest account | 8.0 / 13.8 / 18.4 ms; 673 req/s, p95 43 ms | 4.4 / 6.0 / 9.5 ms; 661 req/s, p95 37 ms |
| history, typical account | 1.7 / 1.9 / 2.6 ms; 2,684 req/s, p95 9 ms | 2.1 / 2.7 / 3.8 ms; 1,550 req/s, p95 16 ms |
| holders, native, latest | 14.9 / 17.6 / 20.0 ms; 271 req/s, p95 79 ms | 2.8 / 4.0 / 6.9 ms; 930 req/s, p95 25 ms |
| holders, native, past round | 32.7 / 37.7 / 42.6 ms; 64 req/s, p95 297 ms | 8.9 / 10.1 / 12.5 ms; 255 req/s, p95 95 ms |
| holders, busiest ASA, latest | 9.9 / 11.0 / 15.3 ms; 305 req/s, p95 81 ms | 2.5 / 3.0 / 3.3 ms; 946 req/s, p95 26 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.5 / 3.0 ms; 1,918 req/s, p95 12 ms | 1.6 / 1.8 / 1.9 ms; 2,178 req/s, p95 10 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 6,241 req/s, p95 4 ms | 0.6 / 0.8 / 1.1 ms; 4,294 req/s, p95 6 ms |

## voi

Measured on 2026-11-23 at commit `ee61ce36`: the 100,000 newest rounds of voi (chain rounds 24,704,546 to 24,804,545) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 1,148 rounds/s | 4,704 rounds/s |
| Effects written | 124,093 (1,424/s) | 124,093 (5,837/s) |
| Time for the range | 87.1 s | 21.3 s |
| Stored size | 9.3 MiB | 70.3 MiB |
| Per round | 0.10 KiB | 0.72 KiB |
| Per effect | 79 bytes | 592 bytes |

Block download is the same for both and not part of the table: 40 rounds/s from the public archival nodes (18,200 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 5.5 MiB, effects_by_token_bucket 3.7 MiB, effect_buckets_by_account_token 0.1 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 39.6 MiB, effects_by_token_bucket 29.7 MiB, holdings_by_token 0.3 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 43 ARC-200 tokens, 9 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 108,989 | 118 | 168 |
| arc200:390001 |  | 4,656 | 17 | 38 |
| arc200:395614 |  | 1,848 | 14 | 21 |
| arc200:47138068 |  | 1,443 | 15 | 20 |
| arc200:300279 |  | 1,436 | 30 | 32 |
| arc200:410811 |  | 740 | 13 | 15 |
| arc200:412682 |  | 618 | 12 | 14 |
| arc200:420069 |  | 548 | 17 | 29 |
| arc200:410111 |  | 462 | 15 | 21 |
| asa:586896 |  | 432 | 1 | 2 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.2 / 2.4 / 3.1 ms; 2,911 req/s, p95 10 ms | 0.8 / 0.9 / 1.1 ms; 3,044 req/s, p95 10 ms |
| account holdings | 1.8 / 2.1 / 2.8 ms; 1,848 req/s, p95 15 ms | 1.8 / 2.2 / 2.5 ms; 1,562 req/s, p95 16 ms |
| balance, latest | 3.1 / 4.3 / 5.8 ms; 1,325 req/s, p95 20 ms | 2.7 / 3.2 / 3.6 ms; 1,079 req/s, p95 22 ms |
| balance, past round | 3.0 / 3.5 / 3.8 ms; 1,335 req/s, p95 19 ms | 2.3 / 2.7 / 3.1 ms; 1,161 req/s, p95 20 ms |
| history, busiest account | 9.3 / 13.1 / 15.2 ms; 465 req/s, p95 79 ms | 7.1 / 10.4 / 10.7 ms; 597 req/s, p95 54 ms |
| history, typical account | 2.2 / 4.0 / 6.8 ms; 1,441 req/s, p95 20 ms | 1.9 / 3.3 / 3.6 ms; 1,319 req/s, p95 18 ms |
| holders, native, latest | 5.1 / 7.4 / 10.1 ms; 544 req/s, p95 44 ms | 2.8 / 3.7 / 4.3 ms; 921 req/s, p95 24 ms |
| holders, native, past round | 18.3 / 21.8 / 24.9 ms; 91 req/s, p95 214 ms | 8.6 / 9.3 / 10.5 ms; 252 req/s, p95 93 ms |
| holders, busiest ASA, latest | 2.1 / 2.4 / 3.1 ms; 1,807 req/s, p95 13 ms | 1.5 / 1.7 / 2.0 ms; 2,216 req/s, p95 10 ms |
| holders, busiest ARC-200, latest | 2.6 / 3.6 / 4.2 ms; 1,188 req/s, p95 21 ms | 1.8 / 2.3 / 2.5 ms; 1,632 req/s, p95 14 ms |
| token info | 0.8 / 1.0 / 1.2 ms; 4,826 req/s, p95 6 ms | 0.7 / 1.0 / 1.6 ms; 3,900 req/s, p95 7 ms |

