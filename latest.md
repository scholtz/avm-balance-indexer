# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-25 at commit `ee61ce36`: the 10,000 newest rounds of algorand-mainnet (chain rounds 67,277,176 to 67,287,175) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 127 rounds/s | 228 rounds/s |
| Effects written | 679,284 (8,649/s) | 679,284 (15,461/s) |
| Time for the range | 78.5 s | 43.9 s |
| Stored size | 120.0 MiB | 464.0 MiB |
| Per round | 12.29 KiB | 47.51 KiB |
| Per effect | 180 bytes | 696 bytes |
| If all 66 M MainNet rounds were like these | 0.8 TiB | 2.9 TiB |

Block download is the same for both and not part of the table: 296 rounds/s from the public archival nodes (195,986 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 59.0 MiB, effects_by_token_bucket 46.9 MiB, effect_buckets_by_account_token 5.4 MiB, holdings_by_token 2.6 MiB, accounts_touched_by_token 2.6 MiB
- PostgreSQL 17.12: effects_by_account_token 243.5 MiB, effects_by_token_bucket 183.0 MiB, holdings_by_token 11.5 MiB, accounts_touched_by_token 11.5 MiB, holdings_by_account 10.5 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 2 ARC-200 tokens, 9,886 ASAs. 4,283 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 462,922 | 5,912 | 17,866 |
| asa:849191641 |  | 56,630 | 3,986 | 10,937 |
| asa:127746157 |  | 34,432 | 0 | 1 |
| asa:31566704 |  | 22,154 | 248 | 5,401 |
| asa:3145862805 |  | 21,450 | 3,087 | 3,088 |
| asa:127745593 |  | 21,136 | 0 | 1 |
| asa:127746786 |  | 10,456 | 0 | 1 |
| asa:3737581431 |  | 4,800 | 23 | 24 |
| asa:1284444444 |  | 4,072 | 11 | 21 |
| asa:3178895177 |  | 1,773 | 40 | 145 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 2.1 / 3.6 ms; 3,599 req/s, p95 9 ms | 0.8 / 1.0 / 1.1 ms; 3,191 req/s, p95 9 ms |
| account holdings | 1.8 / 4.1 / 6.3 ms; 1,993 req/s, p95 15 ms | 1.9 / 2.7 / 3.6 ms; 1,511 req/s, p95 16 ms |
| balance, latest | 2.6 / 5.5 / 7.3 ms; 1,447 req/s, p95 18 ms | 3.0 / 4.1 / 4.8 ms; 1,053 req/s, p95 23 ms |
| balance, past round | 2.8 / 4.6 / 8.0 ms; 1,510 req/s, p95 19 ms | 2.5 / 3.3 / 3.9 ms; 1,131 req/s, p95 21 ms |
| history, busiest account | 7.2 / 12.1 / 17.9 ms; 617 req/s, p95 54 ms | 7.0 / 10.1 / 11.1 ms; 544 req/s, p95 58 ms |
| history, typical account | 1.3 / 4.1 / 7.3 ms; 2,609 req/s, p95 13 ms | 1.9 / 2.2 / 2.6 ms; 1,637 req/s, p95 15 ms |
| holders, native, latest | 15.6 / 28.0 / 32.8 ms; 335 req/s, p95 64 ms | 2.9 / 3.8 / 4.3 ms; 879 req/s, p95 27 ms |
| holders, native, past round | 28.9 / 41.3 / 57.6 ms; 81 req/s, p95 241 ms | 9.3 / 10.1 / 12.6 ms; 241 req/s, p95 96 ms |
| holders, busiest ASA, latest | 10.5 / 17.2 / 26.1 ms; 340 req/s, p95 64 ms | 2.6 / 3.3 / 5.4 ms; 915 req/s, p95 25 ms |
| holders, busiest ARC-200, latest | 1.7 / 5.9 / 11.9 ms; 2,348 req/s, p95 12 ms | 1.6 / 1.9 / 2.2 ms; 2,048 req/s, p95 10 ms |
| token info | 0.5 / 0.9 / 1.4 ms; 7,733 req/s, p95 5 ms | 0.7 / 0.9 / 0.9 ms; 4,875 req/s, p95 5 ms |

## voi

Measured on 2026-11-25 at commit `ee61ce36`: the 100,000 newest rounds of voi (chain rounds 24,767,118 to 24,867,117) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 906 rounds/s | 4,720 rounds/s |
| Effects written | 142,083 (1,288/s) | 142,083 (6,706/s) |
| Time for the range | 110.3 s | 21.2 s |
| Stored size | 6.7 MiB | 75.1 MiB |
| Per round | 0.07 KiB | 0.77 KiB |
| Per effect | 49 bytes | 554 bytes |

Block download is the same for both and not part of the table: 55 rounds/s from the public archival nodes (6,474 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 4.0 MiB, effects_by_token_bucket 2.6 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 42.4 MiB, effects_by_token_bucket 32.1 MiB, holdings_by_account 0.1 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 29 ARC-200 tokens, 4 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 139,718 | 82 | 120 |
| arc200:390001 |  | 524 | 5 | 19 |
| arc200:395614 |  | 308 | 10 | 13 |
| arc200:47138068 |  | 305 | 7 | 12 |
| asa:586896 |  | 254 | 1 | 2 |
| arc200:300279 |  | 218 | 24 | 27 |
| arc200:420069 |  | 74 | 8 | 14 |
| arc200:404004 |  | 70 | 14 | 17 |
| arc200:411311 |  | 66 | 13 | 14 |
| arc200:416000 |  | 66 | 13 | 14 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 1.7 / 4.9 ms; 4,194 req/s, p95 8 ms | 0.8 / 0.9 / 1.0 ms; 3,437 req/s, p95 8 ms |
| account holdings | 1.5 / 3.0 / 7.2 ms; 2,192 req/s, p95 13 ms | 1.8 / 2.4 / 3.1 ms; 1,551 req/s, p95 17 ms |
| balance, latest | 2.1 / 4.5 / 5.9 ms; 1,689 req/s, p95 16 ms | 2.6 / 3.8 / 5.5 ms; 1,140 req/s, p95 21 ms |
| balance, past round | 2.2 / 4.2 / 7.1 ms; 1,627 req/s, p95 17 ms | 2.2 / 2.7 / 3.2 ms; 1,294 req/s, p95 18 ms |
| history, busiest account | 5.9 / 9.8 / 14.2 ms; 612 req/s, p95 56 ms | 4.4 / 6.0 / 6.7 ms; 709 req/s, p95 34 ms |
| history, typical account | 1.9 / 3.8 / 7.8 ms; 1,861 req/s, p95 16 ms | 2.3 / 3.8 / 4.6 ms; 1,053 req/s, p95 23 ms |
| holders, native, latest | 2.6 / 4.6 / 7.0 ms; 926 req/s, p95 29 ms | 2.7 / 3.7 / 4.2 ms; 1,063 req/s, p95 21 ms |
| holders, native, past round | 8.5 / 13.9 / 16.6 ms; 185 req/s, p95 116 ms | 8.4 / 9.4 / 11.2 ms; 250 req/s, p95 96 ms |
| holders, busiest ASA, latest | 1.3 / 2.5 / 4.5 ms; 2,514 req/s, p95 12 ms | 1.7 / 2.8 / 3.7 ms; 2,219 req/s, p95 10 ms |
| holders, busiest ARC-200, latest | 1.6 / 3.1 / 5.5 ms; 2,327 req/s, p95 12 ms | 1.7 / 2.0 / 2.5 ms; 1,975 req/s, p95 11 ms |
| token info | 0.6 / 1.4 / 8.2 ms; 9,639 req/s, p95 4 ms | 0.6 / 0.7 / 0.8 ms; 4,569 req/s, p95 6 ms |

