# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-22 at commit `ee61ce36`: the 10,000 newest rounds of algorand-mainnet (chain rounds 67,182,066 to 67,192,065) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 98 rounds/s | 192 rounds/s |
| Effects written | 576,186 (5,645/s) | 576,186 (11,041/s) |
| Time for the range | 102.1 s | 52.2 s |
| Stored size | 103.6 MiB | 403.9 MiB |
| Per round | 10.61 KiB | 41.35 KiB |
| Per effect | 183 bytes | 711 bytes |
| If all 66 M MainNet rounds were like these | 0.7 TiB | 2.5 TiB |

Block download is the same for both and not part of the table: 312 rounds/s from the public archival nodes (139,422 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 51.6 MiB, effects_by_token_bucket 37.9 MiB, effect_buckets_by_account_token 5.5 MiB, holdings_by_token 2.7 MiB, accounts_touched_by_token 2.7 MiB
- PostgreSQL 17.12: effects_by_account_token 208.4 MiB, effects_by_token_bucket 155.4 MiB, holdings_by_token 12.4 MiB, accounts_touched_by_token 12.2 MiB, holdings_by_account 11.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 3 ARC-200 tokens, 11,688 ASAs. 3,253 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 394,990 | 4,806 | 17,363 |
| asa:849191641 |  | 46,424 | 3,047 | 8,921 |
| asa:31566704 |  | 37,966 | 373 | 7,346 |
| asa:3145862805 |  | 16,113 | 2,034 | 2,035 |
| asa:246516580 |  | 6,313 | 52 | 95 |
| asa:1284444444 |  | 5,402 | 17 | 28 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:3178895177 |  | 3,288 | 65 | 216 |
| asa:2726252423 |  | 2,126 | 50 | 69 |
| asa:246519683 |  | 1,798 | 36 | 67 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.5 / 3.3 / 6.1 ms; 2,991 req/s, p95 10 ms | 0.6 / 0.8 / 0.8 ms; 4,035 req/s, p95 7 ms |
| account holdings | 2.4 / 4.5 / 7.0 ms; 1,719 req/s, p95 16 ms | 1.4 / 1.8 / 2.6 ms; 2,021 req/s, p95 12 ms |
| balance, latest | 3.1 / 4.5 / 5.8 ms; 1,302 req/s, p95 19 ms | 2.3 / 2.8 / 3.5 ms; 1,393 req/s, p95 18 ms |
| balance, past round | 2.8 / 3.7 / 5.3 ms; 1,278 req/s, p95 20 ms | 1.8 / 2.7 / 3.1 ms; 1,614 req/s, p95 14 ms |
| history, busiest account | 8.5 / 14.9 / 17.8 ms; 676 req/s, p95 43 ms | 5.2 / 8.1 / 9.3 ms; 517 req/s, p95 52 ms |
| history, typical account | 1.7 / 1.9 / 2.4 ms; 2,565 req/s, p95 10 ms | 1.5 / 1.9 / 2.9 ms; 2,006 req/s, p95 13 ms |
| holders, native, latest | 17.3 / 20.5 / 25.9 ms; 251 req/s, p95 78 ms | 2.4 / 3.2 / 3.9 ms; 1,096 req/s, p95 22 ms |
| holders, native, past round | 31.8 / 40.0 / 46.3 ms; 61 req/s, p95 333 ms | 8.0 / 8.8 / 12.4 ms; 292 req/s, p95 81 ms |
| holders, busiest ASA, latest | 13.3 / 14.8 / 17.7 ms; 303 req/s, p95 67 ms | 2.2 / 2.8 / 4.9 ms; 1,124 req/s, p95 20 ms |
| holders, busiest ARC-200, latest | 2.5 / 3.4 / 4.0 ms; 1,831 req/s, p95 13 ms | 1.2 / 1.4 / 1.6 ms; 2,753 req/s, p95 8 ms |
| token info | 0.7 / 0.9 / 1.0 ms; 5,959 req/s, p95 5 ms | 0.5 / 0.6 / 0.6 ms; 5,938 req/s, p95 5 ms |

## voi

Measured on 2026-11-22 at commit `ee61ce36`: the 100,000 newest rounds of voi (chain rounds 24,672,228 to 24,772,227) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 1,270 rounds/s | 3,373 rounds/s |
| Effects written | 192,909 (2,450/s) | 192,909 (6,508/s) |
| Time for the range | 78.7 s | 29.6 s |
| Stored size | 20.8 MiB | 109.9 MiB |
| Per round | 0.21 KiB | 1.13 KiB |
| Per effect | 113 bytes | 597 bytes |

Block download is the same for both and not part of the table: 64 rounds/s from the public archival nodes (65,016 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 11.2 MiB, effects_by_token_bucket 9.4 MiB, effect_buckets_by_account_token 0.1 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 60.2 MiB, effects_by_token_bucket 48.8 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 43 ARC-200 tokens, 9 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 177,860 | 128 | 190 |
| arc200:390001 |  | 4,662 | 17 | 39 |
| arc200:395614 |  | 1,852 | 15 | 22 |
| arc200:47138068 |  | 1,447 | 15 | 21 |
| arc200:300279 |  | 1,438 | 29 | 31 |
| arc200:410811 |  | 740 | 13 | 15 |
| arc200:412682 |  | 618 | 12 | 14 |
| arc200:420069 |  | 536 | 16 | 28 |
| arc200:410111 |  | 462 | 15 | 21 |
| arc200:40153155 |  | 412 | 8 | 15 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.1 / 1.5 / 1.7 ms; 3,258 req/s, p95 10 ms | 0.6 / 0.8 / 0.8 ms; 4,267 req/s, p95 6 ms |
| account holdings | 1.8 / 2.0 / 2.7 ms; 1,691 req/s, p95 17 ms | 1.4 / 1.8 / 2.5 ms; 2,148 req/s, p95 12 ms |
| balance, latest | 2.8 / 3.3 / 3.6 ms; 1,365 req/s, p95 19 ms | 2.2 / 2.9 / 3.4 ms; 1,451 req/s, p95 16 ms |
| balance, past round | 2.8 / 3.2 / 3.6 ms; 1,476 req/s, p95 17 ms | 1.9 / 2.4 / 2.9 ms; 1,514 req/s, p95 15 ms |
| history, busiest account | 8.7 / 11.8 / 13.5 ms; 556 req/s, p95 62 ms | 6.3 / 9.2 / 10.0 ms; 846 req/s, p95 31 ms |
| history, typical account | 1.8 / 3.3 / 3.9 ms; 1,715 req/s, p95 16 ms | 1.8 / 3.1 / 3.5 ms; 1,501 req/s, p95 17 ms |
| holders, native, latest | 5.7 / 7.3 / 11.0 ms; 510 req/s, p95 45 ms | 2.3 / 3.9 / 5.8 ms; 1,165 req/s, p95 19 ms |
| holders, native, past round | 19.1 / 24.0 / 27.1 ms; 106 req/s, p95 187 ms | 7.4 / 8.2 / 8.9 ms; 329 req/s, p95 70 ms |
| holders, busiest ASA, latest | 2.6 / 3.4 / 5.1 ms; 1,363 req/s, p95 18 ms | 1.2 / 1.3 / 1.7 ms; 2,540 req/s, p95 9 ms |
| holders, busiest ARC-200, latest | 2.4 / 3.1 / 3.7 ms; 1,259 req/s, p95 19 ms | 1.3 / 1.7 / 1.9 ms; 2,110 req/s, p95 11 ms |
| token info | 0.7 / 0.9 / 1.0 ms; 5,587 req/s, p95 5 ms | 0.4 / 0.5 / 0.5 ms; 6,208 req/s, p95 4 ms |

