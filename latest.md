# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-10-27 at commit `9e2e5bda`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,366,991 to 66,376,990) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 103 rounds/s | 234 rounds/s |
| Effects written | 865,700 (8,929/s) | 865,700 (20,286/s) |
| Time for the range | 97.0 s | 42.7 s |
| Stored size | 134.4 MiB | 566.4 MiB |
| Per round | 13.76 KiB | 58.00 KiB |
| Per effect | 160 bytes | 676 bytes |
| If all 66 M MainNet rounds were like these | 0.8 TiB | 3.6 TiB |

Block download is the same for both and not part of the table: 251 rounds/s from the public archival nodes (253,000 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 68.9 MiB, effects_by_token_bucket 55.7 MiB, effect_buckets_by_account_token 3.8 MiB, holdings_by_token 1.8 MiB, accounts_touched_by_token 1.8 MiB
- PostgreSQL 17.11: effects_by_account_token 309.2 MiB, effects_by_token_bucket 231.0 MiB, holdings_by_token 8.2 MiB, accounts_touched_by_token 8.2 MiB, holdings_by_account 7.4 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 5,284 ASAs. 2,443 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 615,230 | 4,449 | 14,206 |
| asa:31566704 |  | 64,373 | 383 | 4,115 |
| asa:127746157 |  | 53,553 | 0 | 1 |
| asa:849191641 |  | 30,416 | 1,935 | 6,417 |
| asa:127745593 |  | 29,070 | 0 | 1 |
| asa:127746786 |  | 20,399 | 0 | 1 |
| asa:3145862805 |  | 12,817 | 1,750 | 1,751 |
| asa:1284444444 |  | 5,520 | 20 | 56 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1134696561 |  | 1,067 | 25 | 48 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 2.0 / 2.8 ms; 3,848 req/s, p95 7 ms | 0.7 / 0.9 / 1.7 ms; 3,698 req/s, p95 6 ms |
| account holdings | 2.2 / 4.1 / 8.3 ms; 1,612 req/s, p95 21 ms | 1.9 / 2.3 / 3.0 ms; 1,636 req/s, p95 14 ms |
| balance, latest | 3.2 / 6.2 / 13.0 ms; 1,596 req/s, p95 18 ms | 2.9 / 4.0 / 5.0 ms; 1,137 req/s, p95 22 ms |
| balance, past round | 2.3 / 2.7 / 3.3 ms; 2,310 req/s, p95 11 ms | 2.2 / 3.1 / 3.7 ms; 1,327 req/s, p95 18 ms |
| history, busiest account | 4.1 / 6.4 / 8.7 ms; 670 req/s, p95 44 ms | 5.4 / 8.3 / 9.4 ms; 828 req/s, p95 29 ms |
| history, typical account | 1.5 / 1.7 / 2.2 ms; 3,055 req/s, p95 9 ms | 1.9 / 2.1 / 2.6 ms; 1,558 req/s, p95 18 ms |
| holders, native, latest | 12.3 / 13.5 / 14.7 ms; 310 req/s, p95 69 ms | 2.8 / 3.4 / 4.0 ms; 910 req/s, p95 25 ms |
| holders, native, past round | 34.6 / 37.4 / 38.3 ms; 66 req/s, p95 287 ms | 9.2 / 10.2 / 13.7 ms; 234 req/s, p95 104 ms |
| holders, busiest ASA, latest | 40.9 / 46.0 / 48.6 ms; 119 req/s, p95 158 ms | 2.9 / 3.9 / 4.8 ms; 835 req/s, p95 27 ms |
| token info | 0.6 / 0.7 / 0.7 ms; 9,283 req/s, p95 3 ms | 0.6 / 0.8 / 1.1 ms; 4,844 req/s, p95 6 ms |

## voi

Measured on 2026-10-27 at commit `9e2e5bda`: the 100,000 newest rounds of voi (chain rounds 23,858,803 to 23,958,802) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 7,208 rounds/s | 9,689 rounds/s |
| Effects written | 18,209 (1,313/s) | 18,209 (1,764/s) |
| Time for the range | 13.9 s | 10.3 s |
| Stored size | 2.4 MiB | 14.8 MiB |
| Per round | 0.02 KiB | 0.15 KiB |
| Per effect | 139 bytes | 844 bytes |

Block download is the same for both and not part of the table: 32 rounds/s from the public archival nodes (7,489 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.3 MiB, effects_by_token_bucket 1.0 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 9.3 MiB, effects_by_token_bucket 4.9 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 30 ARC-200 tokens, 7 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 14,228 | 51 | 94 |
| asa:586896 |  | 1,878 | 1 | 2 |
| arc200:390001 |  | 688 | 12 | 22 |
| arc200:420069 |  | 344 | 20 | 24 |
| arc200:395614 |  | 232 | 3 | 12 |
| arc200:410111 |  | 136 | 2 | 22 |
| arc200:47138068 |  | 126 | 7 | 12 |
| asa:302190 |  | 94 | 5 | 8 |
| arc200:410811 |  | 92 | 7 | 11 |
| arc200:300279 |  | 60 | 12 | 16 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 1.1 / 1.5 ms; 4,816 req/s, p95 6 ms | 0.7 / 0.9 / 1.7 ms; 3,726 req/s, p95 6 ms |
| account holdings | 1.7 / 2.9 / 3.9 ms; 2,024 req/s, p95 13 ms | 1.8 / 2.6 / 3.3 ms; 1,720 req/s, p95 14 ms |
| balance, latest | 2.6 / 3.0 / 3.9 ms; 1,816 req/s, p95 14 ms | 2.6 / 3.4 / 4.1 ms; 1,245 req/s, p95 20 ms |
| balance, past round | 2.4 / 3.0 / 3.7 ms; 2,080 req/s, p95 11 ms | 2.2 / 2.9 / 3.4 ms; 1,440 req/s, p95 18 ms |
| history, busiest account | 4.4 / 6.2 / 7.7 ms; 684 req/s, p95 44 ms | 5.9 / 8.5 / 9.2 ms; 721 req/s, p95 36 ms |
| history, typical account | 1.6 / 3.0 / 4.9 ms; 2,716 req/s, p95 11 ms | 1.7 / 2.3 / 3.4 ms; 1,726 req/s, p95 15 ms |
| holders, native, latest | 2.9 / 4.2 / 4.9 ms; 931 req/s, p95 29 ms | 2.3 / 3.0 / 4.1 ms; 1,282 req/s, p95 18 ms |
| holders, native, past round | 6.8 / 8.9 / 10.1 ms; 233 req/s, p95 96 ms | 7.7 / 10.4 / 11.4 ms; 288 req/s, p95 82 ms |
| holders, busiest ASA, latest | 1.7 / 2.0 / 3.0 ms; 1,535 req/s, p95 20 ms | 1.5 / 1.6 / 1.8 ms; 2,061 req/s, p95 12 ms |
| holders, busiest ARC-200, latest | 2.0 / 2.3 / 3.2 ms; 1,579 req/s, p95 16 ms | 1.7 / 1.9 / 2.4 ms; 1,835 req/s, p95 13 ms |
| token info | 0.6 / 0.7 / 0.7 ms; 10,044 req/s, p95 3 ms | 0.6 / 0.7 / 0.7 ms; 5,513 req/s, p95 5 ms |

