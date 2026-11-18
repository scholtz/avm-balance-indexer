# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-18 at commit `ee61ce36`: the 10,000 newest rounds of algorand-mainnet (chain rounds 67,058,615 to 67,068,614) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 15 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 76 rounds/s | 123 rounds/s |
| Effects written | 2,160,639 (16,474/s) | 2,160,639 (26,620/s) |
| Time for the range | 131.2 s | 81.2 s |
| Stored size | 349.0 MiB | 1,430.9 MiB |
| Per round | 35.74 KiB | 146.52 KiB |
| Per effect | 166 bytes | 682 bytes |
| If all 66 M MainNet rounds were like these | 2.2 TiB | 9.0 TiB |

Block download is the same for both and not part of the table: 226 rounds/s from the public archival nodes (448,594 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 189.1 MiB, effects_by_token_bucket 138.3 MiB, effect_buckets_by_account_token 8.6 MiB, holdings_by_account 4.3 MiB, holdings_by_token 4.2 MiB
- PostgreSQL 17.12: effects_by_account_token 786.7 MiB, effects_by_token_bucket 586.3 MiB, holdings_by_token 19.3 MiB, accounts_touched_by_token 19.3 MiB, holdings_by_account 17.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 9 ARC-200 tokens, 10,033 ASAs. 1,726 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 1,130,344 | 6,156 | 39,125 |
| asa:31566704 |  | 170,073 | 432 | 8,662 |
| asa:246516580 |  | 121,493 | 127 | 167 |
| asa:246519683 |  | 72,034 | 79 | 112 |
| asa:849191641 |  | 56,371 | 3,215 | 10,108 |
| asa:127746157 |  | 55,901 | 0 | 1 |
| asa:971381860 |  | 48,234 | 36 | 61 |
| asa:3178895177 |  | 36,502 | 252 | 281 |
| asa:1258524377 |  | 34,905 | 13 | 30 |
| asa:127745593 |  | 31,261 | 0 | 1 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 1.0 / 2.1 / 3.2 ms; 5,061 req/s, p95 6 ms | 0.9 / 1.1 / 1.2 ms; 3,535 req/s, p95 8 ms |
| account holdings | 1.6 / 2.4 / 3.0 ms; 2,317 req/s, p95 13 ms | 1.9 / 2.3 / 3.5 ms; 1,768 req/s, p95 15 ms |
| balance, latest | 2.5 / 3.3 / 4.5 ms; 1,753 req/s, p95 14 ms | 2.5 / 3.1 / 3.7 ms; 1,275 req/s, p95 19 ms |
| balance, past round | 2.3 / 2.7 / 6.0 ms; 1,885 req/s, p95 14 ms | 2.2 / 3.0 / 3.4 ms; 1,319 req/s, p95 18 ms |
| history, busiest account | 4.2 / 8.2 / 11.0 ms; 864 req/s, p95 35 ms | 6.9 / 9.6 / 10.3 ms; 760 req/s, p95 33 ms |
| history, typical account | 1.5 / 1.8 / 2.6 ms; 3,922 req/s, p95 7 ms | 1.9 / 2.3 / 2.6 ms; 1,898 req/s, p95 13 ms |
| holders, native, latest | 16.3 / 20.1 / 21.9 ms; 307 req/s, p95 62 ms | 2.6 / 3.5 / 3.9 ms; 1,034 req/s, p95 22 ms |
| holders, native, past round | 19.4 / 23.2 / 28.6 ms; 128 req/s, p95 145 ms | 7.5 / 8.3 / 9.2 ms; 328 req/s, p95 72 ms |
| holders, busiest ASA, latest | 67.2 / 70.3 / 78.0 ms; 88 req/s, p95 200 ms | 3.0 / 3.5 / 3.7 ms; 791 req/s, p95 29 ms |
| holders, busiest ARC-200, latest | 1.4 / 1.6 / 2.3 ms; 2,841 req/s, p95 9 ms | 1.3 / 1.6 / 1.9 ms; 2,690 req/s, p95 8 ms |
| token info | 0.5 / 0.6 / 0.7 ms; 11,027 req/s, p95 3 ms | 0.5 / 0.6 / 0.6 ms; 5,514 req/s, p95 5 ms |

## voi

Measured on 2026-11-18 at commit `ee61ce36`: the 100,000 newest rounds of voi (chain rounds 24,548,374 to 24,648,373) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 15 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.12 |
|---|---:|---:|
| Indexing speed | 5,262 rounds/s | 8,006 rounds/s |
| Effects written | 30,668 (1,614/s) | 30,668 (2,455/s) |
| Time for the range | 19.0 s | 12.5 s |
| Stored size | 2.0 MiB | 19.0 MiB |
| Per round | 0.02 KiB | 0.19 KiB |
| Per effect | 70 bytes | 649 bytes |

Block download is the same for both and not part of the table: 68 rounds/s from the public archival nodes (5,118 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.2 MiB, effects_by_token_bucket 0.8 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, holdings_by_account 0.0 MiB
- PostgreSQL 17.12: effects_by_account_token 11.2 MiB, effects_by_token_bucket 7.3 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 10 ARC-200 tokens, 2 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 30,188 | 50 | 71 |
| asa:586896 |  | 130 | 1 | 2 |
| arc200:300279 |  | 62 | 13 | 14 |
| arc200:404004 |  | 62 | 13 | 14 |
| arc200:411311 |  | 62 | 13 | 14 |
| arc200:416000 |  | 62 | 13 | 14 |
| arc200:420019 |  | 62 | 13 | 14 |
| arc200:420069 |  | 14 | 3 | 4 |
| asa:302189 |  | 11 | 1 | 2 |
| arc200:8471125 |  | 8 | 2 | 3 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 1.1 / 1.1 ms; 4,601 req/s, p95 7 ms | 0.8 / 1.0 / 1.1 ms; 3,494 req/s, p95 9 ms |
| account holdings | 1.4 / 1.6 / 2.1 ms; 2,591 req/s, p95 11 ms | 1.7 / 2.1 / 2.8 ms; 1,898 req/s, p95 14 ms |
| balance, latest | 2.2 / 2.6 / 2.8 ms; 2,063 req/s, p95 12 ms | 2.2 / 2.9 / 3.3 ms; 1,357 req/s, p95 18 ms |
| balance, past round | 2.3 / 2.7 / 3.0 ms; 1,993 req/s, p95 12 ms | 2.1 / 2.7 / 3.4 ms; 1,486 req/s, p95 16 ms |
| history, busiest account | 6.9 / 8.6 / 9.0 ms; 892 req/s, p95 30 ms | 6.5 / 9.4 / 10.8 ms; 446 req/s, p95 64 ms |
| history, typical account | 1.4 / 2.7 / 3.0 ms; 2,202 req/s, p95 13 ms | 1.6 / 3.1 / 3.9 ms; 1,676 req/s, p95 16 ms |
| holders, native, latest | 2.0 / 2.7 / 3.2 ms; 1,342 req/s, p95 21 ms | 1.9 / 2.8 / 3.6 ms; 1,604 req/s, p95 14 ms |
| holders, native, past round | 4.7 / 6.4 / 8.9 ms; 352 req/s, p95 59 ms | 6.3 / 7.0 / 7.2 ms; 393 req/s, p95 60 ms |
| holders, busiest ASA, latest | 1.3 / 1.5 / 1.8 ms; 3,200 req/s, p95 8 ms | 1.3 / 1.4 / 1.5 ms; 2,899 req/s, p95 7 ms |
| holders, busiest ARC-200, latest | 1.5 / 1.8 / 2.3 ms; 2,310 req/s, p95 11 ms | 1.4 / 1.7 / 2.1 ms; 2,363 req/s, p95 9 ms |
| token info | 0.5 / 0.6 / 0.7 ms; 8,818 req/s, p95 3 ms | 0.5 / 0.7 / 1.0 ms; 4,751 req/s, p95 6 ms |

