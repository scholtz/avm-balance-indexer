using System.Globalization;
using System.Runtime.InteropServices;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Hosting;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using Cassandra;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AvmBalanceIndexer.Benchmark;

/// <summary>Settings of one benchmark run.</summary>
public sealed record RunOptions(
    string Provider,
    string Chain,
    int Rounds,
    ulong MarginBehindTip,
    ulong? LastRound,
    string? PostgreSql,
    string? Cassandra,
    string CassandraDatacenter,
    string? CassandraContainer,
    string? ApiDll,
    string OutputDirectory,
    string Commit);

/// <summary>Index the newest rounds into one provider, measure the stored size, then load-test the API on top of it.</summary>
public static class Runner
{
    public static async Task<BenchmarkResult> RunAsync(RunOptions options, CancellationToken cancellationToken)
    {
        var isPostgres = string.Equals(options.Provider, "postgres", StringComparison.OrdinalIgnoreCase);
        if (!isPostgres && !string.Equals(options.Provider, "cassandra", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unknown provider '{options.Provider}'; use postgres or cassandra.");
        }

        var (blocks, download) = await BlockSet.DownloadAsync(options.Chain, options.Rounds, options.MarginBehindTip, options.LastRound, cancellationToken).ConfigureAwait(false);

        var run = $"avm_bench_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var settings = isPostgres ? await PreparePostgreSqlAsync(options, run, cancellationToken).ConfigureAwait(false) : PrepareCassandra(options, run);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddChainStorage(configuration);
            await using var provider = services.BuildServiceProvider();
            var storage = provider.GetRequiredService<IChainStorage>();
            await storage.EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

            var normalizer = new LenientNormalizer(new AlgoEffectNormalizer(), blocks.RewardsBeforeFirstRound);
            var seeder = new Seeder();
            seeder.DryRun(blocks, normalizer);
            await seeder.WriteGenesisAsync(storage, blocks, cancellationToken).ConfigureAwait(false);

            Console.WriteLine($"Indexing {blocks.Rounds} rounds into {options.Provider}");
            var indexer = new RoundIndexer(blocks, normalizer, storage, Options.Create(new IndexerOptions()), NullLogger<RoundIndexer>.Instance);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var indexed = await indexer.RunAsync((ulong)blocks.Rounds, cancellationToken).ConfigureAwait(false);
            clock.Stop();
            var indexing = new IndexingResult(
                clock.Elapsed.TotalSeconds,
                indexed.RoundsIndexed / clock.Elapsed.TotalSeconds,
                indexed.EffectsWritten,
                indexed.EffectsWritten / clock.Elapsed.TotalSeconds,
                seeder.SeededAccounts);
            Console.WriteLine($"Indexed in {indexing.Seconds:F1} s: {indexing.RoundsPerSecond:F0} rounds/s, {indexing.Effects} effects ({indexing.EffectsPerSecond:F0}/s)");

            var effects = indexed.EffectsWritten + seeder.SeededAccounts;
            var (size, version) = isPostgres
                ? await SizeProbe.PostgreSqlAsync(settings["Storage:PostgreSql:ConnectionString"]!, effects, blocks.Rounds, cancellationToken).ConfigureAwait(false)
                : await SizeProbe.CassandraAsync(HostOf(options.Cassandra!), PortOf(options.Cassandra!), options.CassandraDatacenter, run, options.CassandraContainer, effects, blocks.Rounds, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Stored size: {size.TotalBytes / 1048576.0:F1} MiB ({size.BytesPerEffect:F0} bytes per effect)");

            var api = new List<EndpointResult>();
            if (options.ApiDll is not null)
            {
                api.AddRange(await MeasureApiAsync(options, settings, blocks, seeder, cancellationToken).ConfigureAwait(false));
            }

            AssetStatistics? assets = null;
            try
            {
                assets = await CollectAssetsAsync(storage, seeder, blocks.Rounds, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The statistics are extra: a storage timeout while scanning a huge token must not discard hours of measurements.
                Console.Error.WriteLine($"Asset statistics skipped: {exception.Message}");
                Console.Error.WriteLine("::warning::Asset statistics skipped, see the log");
            }

            var machine = $"{RuntimeInformation.OSDescription.Trim()}, {Environment.ProcessorCount} cores, {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0:F0} GiB";
            return new BenchmarkResult(
                options.Provider.ToLowerInvariant(),
                version,
                options.Chain,
                DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                options.Commit,
                machine,
                blocks.Rounds,
                blocks.FirstChainRound,
                blocks.FirstChainRound + (ulong)blocks.Rounds - 1,
                download,
                indexing,
                size,
                api,
                assets);
        }
        finally
        {
            await CleanUpAsync(options, run).ConfigureAwait(false);
        }
    }

    private const int TopTokens = 10;

    /// <summary>Counts the tokens the range touched per kind and the holders of the busiest ones, read back through the storage contract.</summary>
    public static async Task<AssetStatistics> CollectAssetsAsync(IChainStorage storage, Seeder seeder, int rounds, CancellationToken cancellationToken)
    {
        var perToken = seeder.Touches.GroupBy(static p => p.Key.Token).ToDictionary(static g => g.Key, static g => g.Sum(static p => (long)p.Value));
        var byKind = perToken.Keys.GroupBy(static t => t.Kind.ToStorageName()).ToDictionary(static g => g.Key, static g => g.Count());

        var created = 0;
        var arc20 = 0;
        var records = new Dictionary<TokenKey, TokenInfo?>();
        foreach (var token in perToken.Keys.Where(static t => t.Kind == TokenKind.Asa))
        {
            var record = await storage.GetAsync(token, cancellationToken).ConfigureAwait(false);
            records[token] = record;
            if (record is { CreatedRound: > 0 })
            {
                created++;
                arc20 += record.Standard == TokenStandard.Arc20 ? 1 : 0;
            }
        }

        var top = new List<TopToken>();
        foreach (var (token, effects) in perToken.OrderByDescending(static p => p.Value).ThenBy(static p => p.Key.Kind).ThenBy(static p => p.Key.TokenId).Take(TopTokens))
        {
            var (holders, optedIn) = await CountHoldersAsync(storage, token, (ulong)rounds, cancellationToken).ConfigureAwait(false);
            if (!records.TryGetValue(token, out var info))
            {
                info = await storage.GetAsync(token, cancellationToken).ConfigureAwait(false);
            }

            var name = string.IsNullOrEmpty(info?.Symbol) ? info?.Name : info.Symbol;
            top.Add(new TopToken(token.ToString(), string.IsNullOrEmpty(name) ? null : name, effects, holders, optedIn));
        }

        var statistics = new AssetStatistics(byKind, created, arc20, top);
        var largest = statistics.Top.Count > 0 ? statistics.Top[0] : null;
        Console.WriteLine($"Tokens touched: {string.Join(", ", byKind.Select(static p => $"{p.Key} {p.Value}"))}; top token {largest?.Token} has {largest?.Holders} holders");
        return statistics;
    }

    /// <summary>One scan including zero balances gives both numbers: accounts opted in, and those with a non-zero balance.</summary>
    private static async Task<(long NonZero, long OptedIn)> CountHoldersAsync(IChainStorage storage, TokenKey token, ulong round, CancellationToken cancellationToken)
    {
        long nonZero = 0;
        long count = 0;
        string? cursor = null;
        do
        {
            var page = await storage.GetHoldersAtRoundAsync(token, round, true, cursor, 1000, cancellationToken).ConfigureAwait(false);
            count += page.Items.Count;
            nonZero += page.Items.Count(static h => !h.Amount.IsZero);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return (nonZero, count);
    }

    private static async Task<IReadOnlyList<EndpointResult>> MeasureApiAsync(RunOptions options, Dictionary<string, string?> settings, BlockSet blocks, Seeder seeder, CancellationToken cancellationToken)
    {
        var chain = options.Chain;
        var random = new Random(42);
        var native = seeder.Touches.Where(static p => p.Key.Token == TokenKey.Native).OrderByDescending(static p => p.Value).ToArray();
        var hot = Text(native[0].Key.Account);
        var cold = native.Skip(native.Length / 10).OrderBy(_ => random.Next()).Take(500).Select(static p => Text(p.Key.Account)).ToArray();
        string? TopToken(TokenKind kind) => seeder.Touches.Where(p => p.Key.Token.Kind == kind).GroupBy(static p => p.Key.Token.TokenId).OrderByDescending(static g => g.Sum(static p => p.Value)).Select(static g => g.Key.ToString(CultureInfo.InvariantCulture)).FirstOrDefault();
        var asa = TopToken(TokenKind.Asa);
        var arc200 = TopToken(TokenKind.Arc200);
        var middle = blocks.Rounds / 2;

        var cases = new List<LoadCase>
        {
            new("status", $"/v1/{chain}/status", _ => $"/v1/{chain}/status"),
            new("account holdings", "/accounts/{a}/tokens", i => $"/v1/{chain}/accounts/{cold[i % cold.Length]}/tokens"),
            new("balance, latest", "/accounts/{a}/tokens/algo/0/balance", i => $"/v1/{chain}/accounts/{cold[i % cold.Length]}/tokens/algo/0/balance"),
            new("balance, past round", "/accounts/{a}/tokens/algo/0/balance?round=N", i => $"/v1/{chain}/accounts/{cold[i % cold.Length]}/tokens/algo/0/balance?round={middle}"),
            new("history, busiest account", "/accounts/{a}/tokens/algo/0/transactions?limit=100", _ => $"/v1/{chain}/accounts/{hot}/tokens/algo/0/transactions?limit=100"),
            new("history, typical account", "/accounts/{a}/tokens/algo/0/transactions?limit=100", i => $"/v1/{chain}/accounts/{cold[i % cold.Length]}/tokens/algo/0/transactions?limit=100"),
            new("holders, native, latest", "/tokens/algo/0/holders?limit=100", _ => $"/v1/{chain}/tokens/algo/0/holders?limit=100"),
            new("holders, native, past round", "/tokens/algo/0/holders?round=N&limit=100", _ => $"/v1/{chain}/tokens/algo/0/holders?round={middle}&limit=100"),
            new("token info", "/tokens/algo/0", _ => $"/v1/{chain}/tokens/algo/0"),
        };

        if (asa is not null)
        {
            cases.Insert(cases.FindIndex(static c => c.Name == "token info"), new("holders, busiest ASA, latest", "/tokens/asa/{id}/holders?limit=100", _ => $"/v1/{chain}/tokens/asa/{asa}/holders?limit=100"));
        }

        if (arc200 is not null)
        {
            cases.Insert(cases.FindIndex(static c => c.Name == "token info"), new("holders, busiest ARC-200, latest", "/tokens/arc200/{id}/holders?limit=100", _ => $"/v1/{chain}/tokens/arc200/{arc200}/holders?limit=100"));
        }

        var environment = settings.Where(static p => p.Value is not null).ToDictionary(static p => p.Key.Replace(":", "__", StringComparison.Ordinal), static p => p.Value!);
        await using var load = await ApiLoad.StartAsync(options.ApiDll!, environment, FreePort(), cancellationToken).ConfigureAwait(false);
        var results = new List<EndpointResult>();
        foreach (var test in cases)
        {
            var result = await load.MeasureAsync(test, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"  {result.Name,-30} p50 {result.P50Ms,7:F1} ms  p95 {result.P95Ms,7:F1} ms  {result.RequestsPerSecond,7:F0} req/s at {result.Concurrency}  errors {result.Errors}/{result.Requests}");
            results.Add(result);
        }

        return results;
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string Text(Address address) => new Algorand.Address(address.ToArray()).EncodeAsString();

    private static async Task<Dictionary<string, string?>> PreparePostgreSqlAsync(RunOptions options, string database, CancellationToken cancellationToken)
    {
        var baseConnection = options.PostgreSql ?? throw new InvalidOperationException("--postgres or AVM_BENCH_POSTGRES is required for the postgres provider.");
        await using (var admin = new NpgsqlConnection(baseConnection))
        {
            await admin.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var connection = new NpgsqlConnectionStringBuilder(baseConnection) { Database = database, MaxPoolSize = 64 }.ConnectionString;
        return new Dictionary<string, string?>
        {
            ["Chain"] = options.Chain,
            ["Storage:Provider"] = "PostgreSql",
            ["Storage:PostgreSql:ConnectionString"] = connection,
        };
    }

    private static Dictionary<string, string?> PrepareCassandra(RunOptions options, string keyspace)
    {
        var contact = options.Cassandra ?? throw new InvalidOperationException("--cassandra or AVM_BENCH_CASSANDRA is required for the cassandra provider.");
        return new Dictionary<string, string?>
        {
            ["Chain"] = options.Chain,
            ["Storage:Provider"] = "Cassandra",
            ["Storage:Cassandra:ContactPoints:0"] = HostOf(contact),
            ["Storage:Cassandra:Port"] = PortOf(contact).ToString(CultureInfo.InvariantCulture),
            ["Storage:Cassandra:Keyspace"] = keyspace,
            ["Storage:Cassandra:LocalDatacenter"] = options.CassandraDatacenter,
            ["Storage:Cassandra:ReplicationFactor"] = "1",
            ["Storage:Cassandra:CreateKeyspace"] = "true",
        };
    }

    private static async Task CleanUpAsync(RunOptions options, string run)
    {
        if (Environment.GetEnvironmentVariable("AVM_BENCH_KEEP") == "1")
        {
            Console.WriteLine($"Keeping {run} (AVM_BENCH_KEEP=1)");
            return;
        }

        try
        {
            if (string.Equals(options.Provider, "postgres", StringComparison.OrdinalIgnoreCase) && options.PostgreSql is { } connection)
            {
                NpgsqlConnection.ClearAllPools();
                await using var admin = new NpgsqlConnection(connection);
                await admin.OpenAsync().ConfigureAwait(false);
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{run}\" WITH (FORCE)", admin);
                await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            else if (options.Cassandra is { } contact)
            {
                using var cluster = Cluster.Builder().AddContactPoint(HostOf(contact)).WithPort(PortOf(contact)).WithLoadBalancingPolicy(new DCAwareRoundRobinPolicy(options.CassandraDatacenter)).Build();
                var session = await cluster.ConnectAsync().ConfigureAwait(false);
                await session.ExecuteAsync(new SimpleStatement($"DROP KEYSPACE IF EXISTS {run}")).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Clean-up of {run} failed: {ex.Message}");
        }
    }

    private static string HostOf(string contact) => contact.Split(':')[0];

    private static int PortOf(string contact) => contact.Contains(':', StringComparison.Ordinal) ? int.Parse(contact.Split(':')[1], CultureInfo.InvariantCulture) : 9042;
}
