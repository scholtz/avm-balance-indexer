
namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// A test that needs the public internet (archival gossip nodes and public algod/indexer endpoints).
/// Set <c>AVM_SKIP_LIVE_TESTS=1</c> to skip these on machines without network access.
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public const string SkipVariable = "AVM_SKIP_LIVE_TESTS";

    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(SkipVariable) == "1")
        {
            Skip = $"Skipped because {SkipVariable}=1.";
        }
    }
}
