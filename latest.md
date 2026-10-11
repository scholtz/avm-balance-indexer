# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-11 at commit `f89b0ab6`: the 10,000 newest rounds of algorand-mainnet (chain rounds 65,883,504 to 65,893,503) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 74 rounds/s | 205 rounds/s |
| Effects written | 992,712 (7,347/s) | 992,712 (20,323/s) |
| Time for the range | 135.1 s | 48.8 s |
| Stored size | 156.3 MiB | 652.2 MiB |
| Per round | 16.00 KiB | 66.79 KiB |
| Per effect | 163 bytes | 679 bytes |
| If all 66 M MainNet rounds were like these | 1.0 TiB | 4.1 TiB |

Block download is the same for both and not part of the table: 174 rounds/s from the public archival nodes (260,165 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 80.8 MiB, effects_by_token_bucket 63.6 MiB, effect_buckets_by_account_token 4.4 MiB, holdings_by_token 2.3 MiB, accounts_touched_by_token 2.2 MiB
- PostgreSQL 17.11: effects_by_account_token 354.8 MiB, effects_by_token_bucket 265.3 MiB, holdings_by_token 9.9 MiB, accounts_touched_by_token 9.8 MiB, holdings_by_account 9.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 4 ARC-200 tokens, 7,798 ASAs. 3,212 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 674,418 | 4,745 | 14,931 |
| asa:31566704 |  | 75,979 | 813 | 5,065 |
| asa:127746157 |  | 52,066 | 0 | 1 |
| asa:127745593 |  | 32,923 | 0 | 1 |
| asa:849191641 |  | 23,400 | 1,167 | 6,004 |
| asa:127746786 |  | 18,255 | 0 | 1 |
| asa:3145862805 |  | 17,620 | 2,209 | 2,210 |
| asa:1284444444 |  | 6,520 | 19 | 38 |
| asa:3203964481 |  | 4,083 | 94 | 124 |
| asa:386192725 |  | 4,042 | 74 | 98 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 2.0 / 4.5 ms; 3,345 req/s, p95 8 ms | 0.7 / 0.9 / 2.3 ms; 4,049 req/s, p95 5 ms |
| account holdings | 1.9 / 3.2 / 4.2 ms; 1,884 req/s, p95 15 ms | 1.8 / 2.1 / 2.9 ms; 1,716 req/s, p95 14 ms |
| balance, latest | 2.8 / 3.9 / 4.8 ms; 1,504 req/s, p95 18 ms | 2.7 / 4.0 / 4.9 ms; 1,132 req/s, p95 23 ms |
| balance, past round | 2.6 / 3.1 / 4.3 ms; 2,105 req/s, p95 11 ms | 2.2 / 3.2 / 4.2 ms; 1,397 req/s, p95 17 ms |
| history, busiest account | 4.5 / 11.6 / 15.1 ms; 610 req/s, p95 56 ms | 5.3 / 7.9 / 9.3 ms; 750 req/s, p95 31 ms |
| history, typical account | 1.8 / 2.2 / 2.6 ms; 2,665 req/s, p95 9 ms | 1.9 / 2.1 / 2.6 ms; 1,548 req/s, p95 18 ms |
| holders, native, latest | 13.3 / 14.9 / 16.2 ms; 293 req/s, p95 68 ms | 2.9 / 3.7 / 4.2 ms; 962 req/s, p95 23 ms |
| holders, native, past round | 43.7 / 49.8 / 56.9 ms; 50 req/s, p95 378 ms | 9.0 / 9.7 / 11.5 ms; 228 req/s, p95 102 ms |
| holders, busiest ASA, latest | 32.5 / 34.5 / 37.8 ms; 146 req/s, p95 129 ms | 2.8 / 3.4 / 3.8 ms; 890 req/s, p95 25 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.4 / 3.1 ms; 1,790 req/s, p95 13 ms | 1.6 / 1.7 / 2.2 ms; 1,956 req/s, p95 12 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 8,080 req/s, p95 3 ms | 0.6 / 0.6 / 0.7 ms; 5,055 req/s, p95 6 ms |

## voi

Measured on 2026-10-11 at commit `f89b0ab6`: the 100,000 newest rounds of voi (chain rounds 23,382,037 to 23,482,036) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 6,299 rounds/s | 10,255 rounds/s |
| Effects written | 16,568 (1,044/s) | 16,568 (1,699/s) |
| Time for the range | 15.9 s | 9.8 s |
| Stored size | 2.2 MiB | 13.7 MiB |
| Per round | 0.02 KiB | 0.14 KiB |
| Per effect | 139 bytes | 859 bytes |

Block download is the same for both and not part of the table: 198 rounds/s from the public archival nodes (6,920 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.2 MiB, effects_by_token_bucket 0.9 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 8.6 MiB, effects_by_token_bucket 4.5 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_token 0.2 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 26 ARC-200 tokens, 15 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 13,472 | 44 | 77 |
| asa:586896 |  | 948 | 1 | 2 |
| arc200:390001 |  | 584 | 8 | 25 |
| arc200:420069 |  | 236 | 18 | 26 |
| arc200:395614 |  | 216 | 5 | 14 |
| asa:302190 |  | 130 | 5 | 9 |
| arc200:300279 |  | 110 | 20 | 24 |
| arc200:404004 |  | 86 | 16 | 21 |
| arc200:47138068 |  | 81 | 6 | 11 |
| arc200:410111 |  | 70 | 11 | 15 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 1.1 / 1.7 ms; 3,893 req/s, p95 6 ms | 0.8 / 1.0 / 1.8 ms; 4,147 req/s, p95 6 ms |
| account holdings | 1.8 / 2.1 / 3.1 ms; 2,034 req/s, p95 14 ms | 1.9 / 2.5 / 3.4 ms; 1,555 req/s, p95 16 ms |
| balance, latest | 2.9 / 3.3 / 3.8 ms; 1,362 req/s, p95 18 ms | 2.9 / 3.6 / 4.2 ms; 1,184 req/s, p95 22 ms |
| balance, past round | 2.7 / 3.2 / 4.2 ms; 1,819 req/s, p95 13 ms | 2.3 / 3.1 / 3.6 ms; 1,408 req/s, p95 18 ms |
| history, busiest account | 6.6 / 8.7 / 9.3 ms; 708 req/s, p95 40 ms | 3.3 / 4.7 / 6.4 ms; 748 req/s, p95 32 ms |
| history, typical account | 1.9 / 3.3 / 3.7 ms; 2,176 req/s, p95 13 ms | 1.9 / 3.8 / 4.6 ms; 1,525 req/s, p95 17 ms |
| holders, native, latest | 3.4 / 4.6 / 5.6 ms; 834 req/s, p95 29 ms | 2.4 / 3.0 / 3.4 ms; 1,299 req/s, p95 18 ms |
| holders, native, past round | 9.4 / 12.3 / 14.0 ms; 186 req/s, p95 111 ms | 7.5 / 8.3 / 9.8 ms; 273 req/s, p95 85 ms |
| holders, busiest ASA, latest | 2.0 / 2.4 / 2.6 ms; 1,836 req/s, p95 13 ms | 1.6 / 1.7 / 1.9 ms; 2,193 req/s, p95 10 ms |
| holders, busiest ARC-200, latest | 2.3 / 2.8 / 3.4 ms; 1,309 req/s, p95 19 ms | 1.8 / 1.9 / 2.6 ms; 1,798 req/s, p95 14 ms |
| token info | 0.7 / 0.8 / 0.9 ms; 7,363 req/s, p95 3 ms | 0.6 / 0.6 / 0.8 ms; 5,984 req/s, p95 4 ms |

