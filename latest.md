# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-28 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,399,071 to 66,409,070) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 155 rounds/s | 216 rounds/s |
| Effects written | 900,698 (13,940/s) | 900,698 (19,490/s) |
| Time for the range | 64.6 s | 46.2 s |
| Stored size | 142.3 MiB | 593.3 MiB |
| Per round | 14.57 KiB | 60.76 KiB |
| Per effect | 163 bytes | 681 bytes |
| If all 66 M MainNet rounds were like these | 0.9 TiB | 3.7 TiB |

Block download is the same for both and not part of the table: 188 rounds/s from the public archival nodes (270,580 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 72.2 MiB, effects_by_token_bucket 58.1 MiB, effect_buckets_by_account_token 4.4 MiB, holdings_by_token 2.2 MiB, accounts_touched_by_token 2.2 MiB
- PostgreSQL 17.11: effects_by_account_token 321.9 MiB, effects_by_token_bucket 240.7 MiB, holdings_by_token 9.3 MiB, accounts_touched_by_token 9.3 MiB, holdings_by_account 8.5 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 2 ARC-200 tokens, 8,552 ASAs. 5,085 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 640,478 | 5,977 | 17,237 |
| asa:31566704 |  | 73,844 | 340 | 5,271 |
| asa:127746157 |  | 53,905 | 0 | 1 |
| asa:849191641 |  | 32,809 | 2,137 | 7,259 |
| asa:127745593 |  | 30,365 | 0 | 1 |
| asa:127746786 |  | 19,335 | 0 | 1 |
| asa:3145862805 |  | 12,515 | 1,784 | 1,785 |
| asa:1284444444 |  | 5,236 | 15 | 23 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:2543151056 |  | 1,502 | 383 | 384 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.7 / 1.9 / 5.0 ms; 6,444 req/s, p95 5 ms | 0.7 / 0.9 / 1.0 ms; 4,860 req/s, p95 5 ms |
| account holdings | 1.2 / 3.0 / 5.4 ms; 3,099 req/s, p95 10 ms | 1.8 / 2.3 / 3.6 ms; 1,934 req/s, p95 13 ms |
| balance, latest | 1.7 / 3.1 / 4.8 ms; 2,130 req/s, p95 13 ms | 2.6 / 3.1 / 3.6 ms; 1,375 req/s, p95 17 ms |
| balance, past round | 1.6 / 2.2 / 3.4 ms; 3,243 req/s, p95 7 ms | 2.0 / 2.6 / 3.1 ms; 1,587 req/s, p95 15 ms |
| history, busiest account | 3.4 / 6.0 / 9.2 ms; 902 req/s, p95 38 ms | 3.1 / 4.2 / 5.1 ms; 836 req/s, p95 29 ms |
| history, typical account | 1.0 / 1.7 / 3.5 ms; 4,258 req/s, p95 8 ms | 1.7 / 2.1 / 2.4 ms; 1,774 req/s, p95 15 ms |
| holders, native, latest | 6.4 / 8.5 / 10.4 ms; 525 req/s, p95 41 ms | 2.7 / 3.5 / 4.5 ms; 1,037 req/s, p95 22 ms |
| holders, native, past round | 16.9 / 19.4 / 21.4 ms; 118 req/s, p95 170 ms | 7.8 / 8.5 / 10.0 ms; 302 req/s, p95 78 ms |
| holders, busiest ASA, latest | 31.8 / 35.8 / 40.5 ms; 149 req/s, p95 126 ms | 2.8 / 3.4 / 3.5 ms; 871 req/s, p95 25 ms |
| holders, busiest ARC-200, latest | 0.9 / 1.2 / 2.2 ms; 3,878 req/s, p95 7 ms | 1.3 / 1.4 / 1.9 ms; 2,948 req/s, p95 8 ms |
| token info | 0.3 / 0.4 / 0.5 ms; 14,876 req/s, p95 2 ms | 0.5 / 0.6 / 0.6 ms; 6,255 req/s, p95 4 ms |

## voi

Measured on 2026-10-28 at commit `9e2e5bda`: the 100,000 newest rounds of voi (chain rounds 23,890,787 to 23,990,786) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 10,134 rounds/s | 10,396 rounds/s |
| Effects written | 20,335 (2,061/s) | 20,335 (2,114/s) |
| Time for the range | 9.9 s | 9.6 s |
| Stored size | 2.6 MiB | 15.9 MiB |
| Per round | 0.03 KiB | 0.16 KiB |
| Per effect | 135 bytes | 813 bytes |

Block download is the same for both and not part of the table: 30 rounds/s from the public archival nodes (7,581 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.5 MiB, effects_by_token_bucket 1.0 MiB, effect_buckets_by_account_token 0.1 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 9.9 MiB, effects_by_token_bucket 5.4 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 36 ARC-200 tokens, 8 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 15,992 | 56 | 110 |
| asa:586896 |  | 1,340 | 1 | 2 |
| arc200:390001 |  | 876 | 13 | 25 |
| arc200:420069 |  | 408 | 19 | 26 |
| arc200:40153155 |  | 306 | 6 | 22 |
| arc200:410111 |  | 160 | 5 | 22 |
| arc200:410811 |  | 122 | 7 | 11 |
| asa:40152679 |  | 104 | 4 | 9 |
| arc200:395614 |  | 100 | 4 | 11 |
| arc200:300279 |  | 90 | 21 | 25 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.6 / 0.8 / 1.4 ms; 8,069 req/s, p95 4 ms | 0.7 / 0.9 / 0.9 ms; 4,764 req/s, p95 5 ms |
| account holdings | 1.2 / 1.7 / 3.8 ms; 3,264 req/s, p95 10 ms | 1.7 / 2.4 / 3.0 ms; 2,006 req/s, p95 12 ms |
| balance, latest | 1.7 / 2.6 / 2.9 ms; 2,905 req/s, p95 9 ms | 2.3 / 2.8 / 3.5 ms; 1,529 req/s, p95 16 ms |
| balance, past round | 1.7 / 2.1 / 2.7 ms; 2,096 req/s, p95 14 ms | 1.9 / 2.3 / 2.8 ms; 1,826 req/s, p95 13 ms |
| history, busiest account | 5.0 / 6.8 / 7.5 ms; 1,008 req/s, p95 30 ms | 3.4 / 4.7 / 5.5 ms; 848 req/s, p95 28 ms |
| history, typical account | 1.0 / 2.0 / 3.0 ms; 3,442 req/s, p95 8 ms | 1.6 / 2.6 / 3.4 ms; 1,911 req/s, p95 13 ms |
| holders, native, latest | 1.8 / 3.2 / 4.3 ms; 1,409 req/s, p95 19 ms | 2.2 / 3.1 / 3.8 ms; 1,514 req/s, p95 15 ms |
| holders, native, past round | 5.3 / 7.3 / 9.8 ms; 248 req/s, p95 97 ms | 6.5 / 7.3 / 7.4 ms; 354 req/s, p95 71 ms |
| holders, busiest ASA, latest | 0.9 / 1.2 / 2.0 ms; 3,401 req/s, p95 9 ms | 1.2 / 1.3 / 1.4 ms; 3,173 req/s, p95 8 ms |
| holders, busiest ARC-200, latest | 1.1 / 1.6 / 2.8 ms; 2,445 req/s, p95 11 ms | 1.4 / 1.7 / 2.0 ms; 2,401 req/s, p95 10 ms |
| token info | 0.3 / 0.4 / 0.4 ms; 16,981 req/s, p95 2 ms | 0.5 / 0.5 / 0.5 ms; 7,881 req/s, p95 3 ms |

