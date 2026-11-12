# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet and Voi rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

## algorand-mainnet

Measured on 2026-11-12 at commit `e3803b2e`: the 10,000 newest rounds of algorand-mainnet (chain rounds 66,870,802 to 66,880,801) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 103 rounds/s | 189 rounds/s |
| Effects written | 953,995 (9,811/s) | 953,995 (17,984/s) |
| Time for the range | 97.2 s | 53.0 s |
| Stored size | 156.4 MiB | 633.0 MiB |
| Per round | 16.02 KiB | 64.82 KiB |
| Per effect | 169 bytes | 683 bytes |
| If all 66 M MainNet rounds were like these | 1.0 TiB | 4.0 TiB |

Block download is the same for both and not part of the table: 228 rounds/s from the public archival nodes (277,386 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 79.5 MiB, effects_by_token_bucket 63.2 MiB, effect_buckets_by_account_token 5.3 MiB, holdings_by_account 2.6 MiB, holdings_by_token 2.5 MiB
- PostgreSQL 17.11: effects_by_account_token 340.5 MiB, effects_by_token_bucket 256.1 MiB, accounts_touched_by_token 11.6 MiB, holdings_by_token 11.6 MiB, holdings_by_account 10.5 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 2 ARC-200 tokens, 6,601 ASAs. 3,634 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 622,474 | 7,022 | 17,569 |
| asa:3145862805 |  | 93,668 | 8,894 | 8,895 |
| asa:849191641 |  | 55,686 | 3,075 | 9,887 |
| asa:127746157 |  | 53,128 | 0 | 1 |
| asa:31566704 |  | 42,050 | 298 | 3,996 |
| asa:127745593 |  | 32,017 | 0 | 1 |
| asa:127746786 |  | 18,085 | 0 | 1 |
| asa:3737581431 |  | 4,800 | 24 | 24 |
| asa:1284444444 |  | 4,537 | 16 | 25 |
| asa:2543151056 |  | 1,590 | 405 | 406 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.9 / 1.9 / 3.4 ms; 4,493 req/s, p95 6 ms | 0.6 / 0.7 / 1.2 ms; 4,713 req/s, p95 5 ms |
| account holdings | 1.8 / 4.6 / 9.1 ms; 1,951 req/s, p95 17 ms | 1.5 / 2.1 / 3.0 ms; 2,124 req/s, p95 12 ms |
| balance, latest | 2.4 / 3.6 / 4.4 ms; 1,555 req/s, p95 19 ms | 2.2 / 2.8 / 3.6 ms; 1,527 req/s, p95 16 ms |
| balance, past round | 2.1 / 2.7 / 3.3 ms; 2,258 req/s, p95 11 ms | 1.9 / 3.0 / 4.1 ms; 1,772 req/s, p95 13 ms |
| history, busiest account | 3.7 / 8.8 / 11.0 ms; 816 req/s, p95 36 ms | 4.3 / 5.8 / 7.0 ms; 821 req/s, p95 35 ms |
| history, typical account | 1.3 / 1.4 / 1.9 ms; 3,434 req/s, p95 8 ms | 1.5 / 2.0 / 2.7 ms; 2,230 req/s, p95 11 ms |
| holders, native, latest | 8.6 / 10.1 / 13.6 ms; 399 req/s, p95 56 ms | 2.2 / 2.9 / 4.6 ms; 1,199 req/s, p95 20 ms |
| holders, native, past round | 26.2 / 30.8 / 36.5 ms; 84 req/s, p95 230 ms | 7.7 / 8.4 / 9.8 ms; 290 req/s, p95 82 ms |
| holders, busiest ASA, latest | 2.8 / 4.0 / 9.1 ms; 791 req/s, p95 36 ms | 2.0 / 2.4 / 3.4 ms; 1,237 req/s, p95 19 ms |
| holders, busiest ARC-200, latest | 1.4 / 1.7 / 2.1 ms; 2,621 req/s, p95 9 ms | 1.2 / 1.4 / 1.8 ms; 2,851 req/s, p95 8 ms |
| token info | 0.4 / 0.5 / 0.6 ms; 9,060 req/s, p95 3 ms | 0.5 / 0.5 / 0.6 ms; 6,581 req/s, p95 4 ms |

## voi

Measured on 2026-11-12 at commit `e3803b2e`: the 100,000 newest rounds of voi (chain rounds 24,361,760 to 24,461,759) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 26.04.1 LTS, 4 cores, 16 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 3,914 rounds/s | 6,930 rounds/s |
| Effects written | 41,025 (1,606/s) | 41,025 (2,843/s) |
| Time for the range | 25.6 s | 14.4 s |
| Stored size | 3.1 MiB | 25.9 MiB |
| Per round | 0.03 KiB | 0.27 KiB |
| Per effect | 80 bytes | 661 bytes |

Block download is the same for both and not part of the table: 66 rounds/s from the public archival nodes (6,845 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.8 MiB, effects_by_token_bucket 1.2 MiB, effect_buckets_by_account_token 0.1 MiB, holdings_by_token 0.0 MiB, holdings_by_account 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 15.4 MiB, effects_by_token_bucket 9.9 MiB, holdings_by_token 0.2 MiB, accounts_touched_by_token 0.2 MiB, holdings_by_account 0.2 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 24 ARC-200 tokens, 9 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 37,819 | 146 | 183 |
| arc200:390001 |  | 792 | 11 | 21 |
| arc200:420069 |  | 396 | 19 | 29 |
| asa:586896 |  | 370 | 1 | 2 |
| arc200:395614 |  | 320 | 5 | 13 |
| arc200:300279 |  | 226 | 19 | 27 |
| arc200:47138068 |  | 190 | 6 | 10 |
| arc200:412682 |  | 158 | 7 | 15 |
| asa:302190 |  | 95 | 5 | 10 |
| arc200:404004 |  | 92 | 17 | 20 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.7 / 0.9 / 2.3 ms; 4,732 req/s, p95 6 ms | 0.6 / 0.7 / 2.0 ms; 4,760 req/s, p95 5 ms |
| account holdings | 1.3 / 2.0 / 4.2 ms; 2,352 req/s, p95 12 ms | 1.4 / 2.0 / 2.8 ms; 2,193 req/s, p95 12 ms |
| balance, latest | 2.1 / 2.6 / 3.4 ms; 2,169 req/s, p95 12 ms | 2.1 / 2.6 / 2.9 ms; 1,581 req/s, p95 16 ms |
| balance, past round | 1.9 / 2.3 / 3.1 ms; 2,192 req/s, p95 11 ms | 1.7 / 2.2 / 2.8 ms; 1,660 req/s, p95 15 ms |
| history, busiest account | 5.2 / 8.4 / 10.7 ms; 934 req/s, p95 30 ms | 4.8 / 6.5 / 7.3 ms; 765 req/s, p95 37 ms |
| history, typical account | 1.3 / 2.0 / 2.3 ms; 3,231 req/s, p95 9 ms | 1.5 / 2.2 / 3.4 ms; 2,203 req/s, p95 11 ms |
| holders, native, latest | 3.1 / 4.4 / 9.2 ms; 789 req/s, p95 32 ms | 2.2 / 2.9 / 3.7 ms; 1,236 req/s, p95 19 ms |
| holders, native, past round | 12.3 / 16.6 / 20.7 ms; 192 req/s, p95 99 ms | 6.7 / 7.4 / 7.8 ms; 335 req/s, p95 69 ms |
| holders, busiest ASA, latest | 1.3 / 1.6 / 2.4 ms; 2,679 req/s, p95 10 ms | 1.2 / 1.3 / 1.5 ms; 3,119 req/s, p95 7 ms |
| holders, busiest ARC-200, latest | 1.6 / 2.0 / 2.6 ms; 1,876 req/s, p95 14 ms | 1.3 / 1.8 / 2.3 ms; 2,523 req/s, p95 9 ms |
| token info | 0.4 / 0.5 / 0.6 ms; 11,076 req/s, p95 2 ms | 0.4 / 0.5 / 0.5 ms; 7,462 req/s, p95 4 ms |

