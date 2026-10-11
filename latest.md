# Latest nightly benchmark

PostgreSQL against Cassandra on the newest MainNet rounds. Updated every night by the workflow `Nightly benchmark`; older results are in `history/`. How it is measured: docs/benchmarks.md on the main branch.

Measured on 2026-10-11 at commit `f6a47219`: the 10,000 newest rounds of algorand-mainnet (chain rounds 65,878,725 to 65,888,724) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Ubuntu 24.04.5 LTS, 4 cores, 16 GiB.

### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 109 rounds/s | 211 rounds/s |
| Effects written | 989,578 (10,762/s) | 989,578 (20,869/s) |
| Time for the range | 91.9 s | 47.4 s |
| Stored size | 158.1 MiB | 650.3 MiB |
| Per round | 16.2 KiB | 66.6 KiB |
| Per effect | 165 bytes | 678 bytes |
| If all 66 M MainNet rounds were like these | 1.0 TiB | 4.1 TiB |

Block download is the same for both and not part of the table: 187 rounds/s from the public archival nodes (263,326 transactions).

### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 81.4 MiB, effects_by_token_bucket 64.4 MiB, effect_buckets_by_account_token 4.7 MiB, holdings_by_token 2.3 MiB, holdings_by_account 2.3 MiB
- PostgreSQL 17.11: effects_by_account_token 353.1 MiB, effects_by_token_bucket 264.8 MiB, holdings_by_token 10.3 MiB, accounts_touched_by_token 10.3 MiB, holdings_by_account 9.4 MiB

### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.7 / 1.3 / 2.2 ms; 4,097 req/s, p95 7 ms | 0.8 / 0.9 / 1.9 ms; 3,943 req/s, p95 6 ms |
| account holdings | 1.4 / 2.6 / 3.5 ms; 2,543 req/s, p95 11 ms | 1.8 / 2.2 / 3.2 ms; 1,565 req/s, p95 15 ms |
| balance, latest | 2.1 / 3.1 / 4.4 ms; 1,972 req/s, p95 16 ms | 2.8 / 3.6 / 3.8 ms; 1,139 req/s, p95 24 ms |
| balance, past round | 1.9 / 2.1 / 2.7 ms; 2,932 req/s, p95 8 ms | 2.2 / 3.0 / 3.4 ms; 1,274 req/s, p95 19 ms |
| history, busiest account | 4.8 / 7.2 / 9.8 ms; 871 req/s, p95 36 ms | 5.2 / 7.8 / 8.4 ms; 747 req/s, p95 33 ms |
| history, typical account | 1.2 / 1.5 / 2.7 ms; 3,683 req/s, p95 8 ms | 1.9 / 2.5 / 2.9 ms; 1,608 req/s, p95 17 ms |
| holders, native, latest | 8.6 / 10.5 / 14.2 ms; 443 req/s, p95 47 ms | 2.8 / 3.5 / 4.5 ms; 943 req/s, p95 24 ms |
| holders, native, past round | 27.4 / 30.9 / 33.1 ms; 85 req/s, p95 233 ms | 8.9 / 9.7 / 11.0 ms; 233 req/s, p95 103 ms |
| holders, busiest ASA, latest | 1.3 / 1.5 / 1.7 ms; 3,014 req/s, p95 8 ms | 1.5 / 1.6 / 1.7 ms; 2,096 req/s, p95 11 ms |
| token info | 0.5 / 0.5 / 0.6 ms; 13,510 req/s, p95 2 ms | 0.6 / 0.7 / 0.8 ms; 5,664 req/s, p95 5 ms |
