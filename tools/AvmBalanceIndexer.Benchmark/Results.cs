using System.Text.Json;
using System.Text.Json.Serialization;

namespace AvmBalanceIndexer.Benchmark;

/// <summary>Everything one benchmark run measures for one storage provider; serialized as the nightly result file.</summary>
public sealed record BenchmarkResult(
    string Provider,
    string ProviderVersion,
    string Chain,
    string Date,
    string Commit,
    string Machine,
    int Rounds,
    ulong FirstChainRound,
    ulong LastChainRound,
    DownloadResult Download,
    IndexingResult Indexing,
    StorageSizeResult Storage,
    IReadOnlyList<EndpointResult> Api,
    AssetStatistics? Assets = null)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Fetching and decoding the blocks from the public archival nodes (independent of the database).</summary>
public sealed record DownloadResult(double Seconds, double RoundsPerSecond, long Transactions);

/// <summary>Turning the blocks into effects and committing them with the production round indexer.</summary>
public sealed record IndexingResult(double Seconds, double RoundsPerSecond, long Effects, double EffectsPerSecond, long SeededAccounts);

/// <summary>Bytes the data occupies after indexing.</summary>
public sealed record StorageSizeResult(long TotalBytes, long Effects, double BytesPerEffect, double KilobytesPerRound, bool IncludesUnflushedData, IReadOnlyList<TableSize> Tables);

public sealed record TableSize(string Name, long Bytes);

/// <summary>Latency and throughput of one API request against the running API process.</summary>
public sealed record EndpointResult(
    string Name,
    string Path,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    int Concurrency,
    double RequestsPerSecond,
    double ConcurrentP95Ms,
    int Requests,
    int Errors);

/// <summary>What the indexed range contains: tokens per kind and the holders of the busiest ones.</summary>
/// <param name="TokensByKind">Distinct tokens with at least one effect in the range, per kind (algo, asa, arc200).</param>
/// <param name="AssetsCreated">ASAs created inside the range (their token record exists).</param>
/// <param name="Arc20Assets">Of those, the ARC-20 Smart ASAs.</param>
/// <param name="Top">The tokens with the most effects, with the number of accounts that hold them at the end of the range.</param>
public sealed record AssetStatistics(IReadOnlyDictionary<string, int> TokensByKind, int AssetsCreated, int Arc20Assets, IReadOnlyList<TopToken> Top);

/// <param name="Token">Kind and id, for example <c>asa:31566704</c>.</param>
/// <param name="Name">Symbol or name from the token record, when the range created the token.</param>
/// <param name="Effects">Effects that touched the token in the range.</param>
/// <param name="Holders">Accounts with a non-zero balance at the last round.</param>
/// <param name="OptedIn">Accounts that hold the token including zero balances.</param>
public sealed record TopToken(string Token, string? Name, long Effects, long Holders, long OptedIn);
