// Run with: dotnet run --project examples/Quickstart
using AvmBalanceIndexer.Client;
using AvmBalanceIndexer.Client.Generated;

// Public TestNet deployment. Use AvmBalanceIndexerClient.Voi() / .AlgorandMainnet(), or set Options.BaseUrl for your own deployment.
using var indexer = AvmBalanceIndexerClient.AlgorandTestnet();

var status = await indexer.GetStatusAsync();
Console.WriteLine($"{status.Chain}: {status.State}, indexed round {status.IndexedRound} of {status.TargetRound}");

// The first five holders of the native token at the latest round (amounts are decimal strings).
var shown = 0;
await foreach (var holder in indexer.EnumerateHoldersAsync(TokenKind.Algo, 0, limit: 100))
{
    Console.WriteLine($"{holder.Account} {System.Numerics.BigInteger.Parse(holder.Amount)}");
    if (++shown == 5)
    {
        break;
    }
}

try
{
    await indexer.GetTokenAsync(TokenKind.Asa, 1);
}
catch (AvmBalanceIndexerApiException error) when (error.IsNotFound)
{
    Console.WriteLine("asset 1 is not known to this chain");
}
