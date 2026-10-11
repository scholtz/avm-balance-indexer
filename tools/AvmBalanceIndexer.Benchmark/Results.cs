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
    IReadOnlyList<EndpointResult> Api)
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
