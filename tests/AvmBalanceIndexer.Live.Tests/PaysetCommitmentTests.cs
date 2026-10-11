using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Indexing.Adapters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Real blocks from every consensus era must reproduce their header payset commitments. The adapter verifies the commitment
/// when it decodes a block, so a successful fetch is the proof; the printout shows which protocols were covered.
/// </summary>
public class PaysetCommitmentTests(ITestOutputHelper output)
{
    private static readonly ulong[] Samples =
        [500, 2_000_000, 6_000_000, 9_000_000, 12_000_000, 15_000_000, 18_000_000, 21_000_000, 24_000_000, 28_000_000, 32_000_000, 36_000_000, 40_000_000, 45_000_000, 50_000_000, 55_000_000, 60_000_000, 65_000_000];

    [LiveFact]
    public Task MainNet_Blocks_Of_Every_Era_Match_Their_Payset_Commitments() => AssertSamplesAsync(ChainId.AlgorandMainnet);

    [LiveFact]
    public Task TestNet_Blocks_Of_Every_Era_Match_Their_Payset_Commitments() => AssertSamplesAsync(ChainId.AlgorandTestnet);

    private async Task AssertSamplesAsync(string chain)
    {
        var http = AlgorandChainFactory.CreateBlockHttpClient();
        var options = new AlgorandChainOptions { Chain = chain, MaxAttempts = 3, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(2) };
        var adapter = AlgorandChainFactory.Create(options, http, NullLoggerFactory.Instance);
        var latest = await adapter.GetLatestRoundAsync(CancellationToken.None);

        var verified = 0;
        var flat = false;
        var merkle = false;
        foreach (var round in Samples.Where(r => r < latest).Append(latest - 10))
        {
            var block = await adapter.FetchBlockAsync(round, CancellationToken.None);
            verified++;
            var header = block.Block;
            var populated = header.Transactions?.Count > 0;
            flat |= populated && ConsensusProtocols.UsesFlatPaysetCommitment(header.CurrentProtocol!);
            merkle |= populated && !ConsensusProtocols.UsesFlatPaysetCommitment(header.CurrentProtocol!) && !ConsensusProtocols.HasNoPaysetCommitment(header.CurrentProtocol!);
            output.WriteLine($"{chain} round {round}: {header.Transactions?.Count ?? 0} txns, protocol {header.CurrentProtocol}");
        }

        Assert.True(verified >= 10);
        Assert.True(flat && merkle, "The samples must include non-empty blocks of both the flat and the Merkle era.");
    }
}
