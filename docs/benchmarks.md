# Benchmarks: PostgreSQL against Cassandra

`tools/AvmBalanceIndexer.Benchmark` measures what an operator needs to choose a database: how fast the indexer commits real MainNet data, how
much disk the data takes, and how fast the API answers on top of it.

## What is measured

1. **Download.** The newest 10,000 rounds (200 rounds behind the tip) are fetched from the public archival nodes once. This is the same for
   both providers and reported separately.
2. **Indexing.** The rounds are replayed as rounds 1 to 10,000 of an empty database through the production `RoundIndexer` (normalizer,
   balance tracker, batched commits). A mid-chain range has no history, so a dry run first finds the balances every account must already hold
   (`Seeder`) and writes them as round 0, exactly like genesis balances; ids, accounts, amounts and transactions are the real ones.
   Reported: rounds, effects and effects per second.
3. **Stored size.** PostgreSQL: `pg_total_relation_size` of every table including indexes, TOAST and partitions, after `VACUUM (ANALYZE)`.
   Cassandra: `nodetool flush` and `system_views.disk_usage` (sstables, compressed, one replica, MiB resolution). The estimate for the whole chain
   multiplies the per-round size by 66 million rounds, an upper bound since early rounds carry far less data.
4. **API.** The real API runs as its own process (`dotnet AvmBalanceIndexer.Api.dll`, rate limit off) and ten request kinds are timed over
   loopback HTTP: 300 sequential requests (p50 / p95 / p99), then 2,000 with 16 in flight (throughput and p95). Accounts are chosen from the data:
   the busiest account, and 500 typical ones drawn from the 90 percent less active.

## Where to find the results

| What | Where |
|---|---|
| Summary of the newest run | Actions, [Nightly benchmark](https://github.com/scholtz/avm-balance-indexer/actions/workflows/benchmark.yml), newest run, summary page |
| Latest comparison table | branch [`benchmark-results`](https://github.com/scholtz/avm-balance-indexer/tree/benchmark-results), `latest.md` |
| Raw numbers of every night | the same branch, `history/YYYY-MM-DD-<provider>.json`, and the run artifacts `bench-postgres` / `bench-cassandra` |
| Snapshot in the README | [PostgreSQL or Cassandra?](../README.md#postgresql-or-cassandra) (updated by hand from `latest.md` when something changes) |

The workflow runs at 02:17 UTC and on demand (Actions, Run workflow, optionally with another number of rounds). PostgreSQL and Cassandra each run
on their own runner with only that database next to the API, so one does not disturb the other. A failed night is visible as a red run.

## Run it yourself

```sh
dotnet build src/AvmBalanceIndexer.Api -c Release && dotnet build tools/AvmBalanceIndexer.Benchmark -c Release

# PostgreSQL (any server; the tool creates and drops its own database)
docker run -d --name bench-pg -e POSTGRES_PASSWORD=pw -p 5432:5432 postgres:17-alpine
dotnet tools/AvmBalanceIndexer.Benchmark/bin/Release/net10.0/AvmBalanceIndexer.Benchmark.dll run --provider postgres   --postgres "Host=localhost;Username=postgres;Password=pw;Database=postgres"   --api-dll src/AvmBalanceIndexer.Api/bin/Release/net10.0/AvmBalanceIndexer.Api.dll --out results

# Cassandra (the container name lets the tool flush memtables so the size is complete)
docker run -d --name bench-cass -e CASSANDRA_DC=dc1 -e CASSANDRA_ENDPOINT_SNITCH=GossipingPropertyFileSnitch -p 9042:9042 cassandra:5.0
dotnet tools/AvmBalanceIndexer.Benchmark/bin/Release/net10.0/AvmBalanceIndexer.Benchmark.dll run --provider cassandra   --cassandra localhost:9042 --cassandra-container bench-cass   --api-dll src/AvmBalanceIndexer.Api/bin/Release/net10.0/AvmBalanceIndexer.Api.dll --out results

# the comparison table
dotnet tools/AvmBalanceIndexer.Benchmark/bin/Release/net10.0/AvmBalanceIndexer.Benchmark.dll report results/bench-cassandra.json results/bench-postgres.json
```

`--rounds` changes the size of the range (default 10000), `AVM_BENCH_KEEP=1` keeps the database or keyspace afterwards.

## Reading the numbers

- Both databases run as a single local node. A production Cassandra cluster has replication (the stored size is per replica, three replicas
  need three times the disk) and quorum reads cost network round trips; a production PostgreSQL has a primary and replicas the same way.
- The API tests run on the machine that holds the database, so absolute latencies are lower than over a network; compare the providers, not the values.
- Holder queries differ most. A past-round holder list is computed from each holder's own history on both providers, which is why it is the
  slowest request; the latest holder list is an index scan on both, ordered by account.
- The size per effect depends on the data of the night; the trend over time is in `history/`.
