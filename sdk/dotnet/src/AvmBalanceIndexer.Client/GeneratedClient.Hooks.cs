namespace AvmBalanceIndexer.Client.Generated;

/// <summary>Per-request settings of the generated client, so that a caller's <see cref="HttpClient"/> never has to be modified.</summary>
internal partial class GeneratedClient
{
    /// <summary>Root URL of the deployment, ending in a slash; the generated code builds relative URLs.</summary>
    public Uri Root { get; init; } = null!;

    /// <summary>Headers added to every request.</summary>
    public IReadOnlyDictionary<string, string> ExtraHeaders { get; init; } = new Dictionary<string, string>();

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
    {
        request.RequestUri = new Uri(Root, url);
        if (!request.Headers.UserAgent.Any())
        {
            request.Headers.UserAgent.ParseAdd("avm-balance-indexer-dotnet-sdk");
        }

        foreach (var (name, value) in ExtraHeaders)
        {
            request.Headers.Remove(name);
            request.Content?.Headers.Remove(name);
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }
    }
}
