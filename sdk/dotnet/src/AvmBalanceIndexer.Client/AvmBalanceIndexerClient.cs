using System.Runtime.CompilerServices;
using AvmBalanceIndexer.Client.Generated;

namespace AvmBalanceIndexer.Client;

/// <summary>Public deployments of the indexer, used when <see cref="AvmBalanceIndexerOptions.BaseUrl"/> is not set.</summary>
public static class DefaultEndpoints
{
    /// <summary>The public deployment that serves <paramref name="chain"/>.</summary>
    public static Uri For(ChainId chain) => chain switch
    {
        ChainId.AlgorandMainnet => new Uri("https://mainnet.balance-indexer.biatec.io"),
        ChainId.AlgorandTestnet => new Uri("https://testnet.balance-indexer.biatec.io"),
        ChainId.Voi => new Uri("https://voi.balance-indexer.biatec.io"),
        _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, "Unknown chain."),
    };
}

/// <summary>Settings of an <see cref="AvmBalanceIndexerClient"/>.</summary>
public sealed class AvmBalanceIndexerOptions
{
    /// <summary>Which chain the indexer at <see cref="BaseUrl"/> serves. Default: <see cref="ChainId.AlgorandMainnet"/>.</summary>
    public ChainId Chain { get; set; } = ChainId.AlgorandMainnet;

    /// <summary>Root URL of an indexer deployment (no <c>/v1/...</c>). Default: the public deployment of <see cref="Chain"/>.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>Your own <see cref="HttpClient"/> (proxy, handler, <c>IHttpClientFactory</c>). It is neither modified nor disposed by the SDK; its <c>BaseAddress</c> and <c>Timeout</c> are not used (use <see cref="BaseUrl"/> and the client's own settings).</summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>Extra headers on every request.</summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>();

    /// <summary>Timeout of a request when the SDK creates the <see cref="HttpClient"/> (ignored when you pass your own). Default 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>The server answered with an error status.</summary>
public sealed class AvmBalanceIndexerApiException : Exception
{
    public AvmBalanceIndexerApiException(int statusCode, string message, TimeSpan? retryAfter, string? body, Exception? inner = null)
        : base($"HTTP {statusCode}: {message}", inner)
    {
        StatusCode = statusCode;
        ApiMessage = message;
        RetryAfter = retryAfter;
        Body = body;
    }

    /// <summary>HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>The <c>error</c> field of the response, or the status text.</summary>
    public string ApiMessage { get; }

    /// <summary>How long to wait before retrying, from <c>Retry-After</c> (429 and 503).</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Raw response body.</summary>
    public string? Body { get; }

    /// <summary>Unknown token, round beyond the indexed one, or wrong chain.</summary>
    public bool IsNotFound => StatusCode == 404;

    /// <summary>Rate limited or nothing indexed yet: retry after <see cref="RetryAfter"/>.</summary>
    public bool IsRetryable => StatusCode is 429 or 503 or 504;
}

/// <summary>The request never produced a response (network failure or timeout).</summary>
public sealed class AvmBalanceIndexerConnectionException(string message, Exception inner) : Exception(message, inner);

/// <summary>A holder scan ended as <c>Failed</c> or <c>Cancelled</c> before it finished.</summary>
public sealed class HolderScanException(string message, ScanResponse scan) : Exception(message)
{
    /// <summary>The scan as last reported by the server.</summary>
    public ScanResponse Scan { get; } = scan;
}

/// <summary>
/// Client for one chain of an AVM Balance Indexer deployment.
/// <code>
/// using var indexer = AvmBalanceIndexerClient.Voi();                                    // public Voi deployment
/// using var custom = new AvmBalanceIndexerClient(new() { Chain = ChainId.AlgorandMainnet, BaseUrl = new("https://indexer.example.com") });
/// </code>
/// </summary>
public sealed class AvmBalanceIndexerClient : IDisposable
{
    private static readonly string[] TerminalScanStates = ["Completed", "Failed", "Cancelled"];

    private readonly GeneratedClient _api;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public AvmBalanceIndexerClient(AvmBalanceIndexerOptions? options = null)
    {
        options ??= new AvmBalanceIndexerOptions();
        Chain = options.Chain;
        BaseUrl = options.BaseUrl ?? DefaultEndpoints.For(options.Chain);

        // A HttpClient of the caller is never modified (it may be shared and may have sent requests already): the address and the headers
        // are applied to each request instead (GeneratedClient.Hooks.cs).
        _ownsHttpClient = options.HttpClient is null;
        _http = options.HttpClient ?? new HttpClient { Timeout = options.Timeout };
        var headers = new Dictionary<string, string>(options.Headers);
        foreach (var (name, value) in headers)
        {
            // Fail now, not silently on every request: a header the framework refuses.
            using var probe = new HttpRequestMessage { Content = new ByteArrayContent([]) };
            if (!probe.Headers.TryAddWithoutValidation(name, value) && !probe.Content.Headers.TryAddWithoutValidation(name, value))
            {
                throw new ArgumentException($"The header \"{name}\" cannot be sent.", nameof(options));
            }
        }

        _api = new GeneratedClient(_http)
        {
            ReadResponseAsString = true,
            Root = new Uri(BaseUrl.AbsoluteUri.TrimEnd('/') + "/"),
            ExtraHeaders = headers,
        };
    }

    /// <summary>The chain this client reads.</summary>
    public ChainId Chain { get; }

    /// <summary>Root URL of the deployment in use.</summary>
    public Uri BaseUrl { get; }

    /// <summary>Public MainNet deployment (or <see cref="AvmBalanceIndexerOptions.BaseUrl"/>).</summary>
    public static AvmBalanceIndexerClient AlgorandMainnet(AvmBalanceIndexerOptions? options = null) => For(ChainId.AlgorandMainnet, options);

    /// <summary>Public TestNet deployment (or <see cref="AvmBalanceIndexerOptions.BaseUrl"/>).</summary>
    public static AvmBalanceIndexerClient AlgorandTestnet(AvmBalanceIndexerOptions? options = null) => For(ChainId.AlgorandTestnet, options);

    /// <summary>Public Voi deployment (or <see cref="AvmBalanceIndexerOptions.BaseUrl"/>).</summary>
    public static AvmBalanceIndexerClient Voi(AvmBalanceIndexerOptions? options = null) => For(ChainId.Voi, options);

    private static AvmBalanceIndexerClient For(ChainId chain, AvmBalanceIndexerOptions? options)
    {
        // The caller's options object is not changed: it can be reused for another chain.
        var copy = new AvmBalanceIndexerOptions { Chain = chain, BaseUrl = options?.BaseUrl, HttpClient = options?.HttpClient, Timeout = options?.Timeout ?? TimeSpan.FromSeconds(30) };
        foreach (var (name, value) in options?.Headers ?? new Dictionary<string, string>())
        {
            copy.Headers[name] = value;
        }

        return new AvmBalanceIndexerClient(copy);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>Indexed round, target round, lag and ingestion state.</summary>
    public Task<StatusResponse> GetStatusAsync(CancellationToken cancellationToken = default) =>
        CallAsync(() => _api.GetChainStatusAsync(Chain, cancellationToken));

    /// <summary>Current holdings of an account (one page).</summary>
    public Task<HoldingsResponse> GetAccountTokensAsync(string address, bool? includeZero = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        CallAsync(() => { ValidateLimit(limit); return _api.GetAccountTokensAsync(Chain, address, includeZero, cursor, limit, cancellationToken); });

    /// <summary>End-of-round balance of an account and token. <c>Amount</c> is a decimal string (use <see cref="System.Numerics.BigInteger.Parse(string)"/>).</summary>
    public Task<BalanceResponse> GetBalanceAsync(string address, TokenKind kind, ulong tokenId, ulong? round = null, CancellationToken cancellationToken = default) =>
        CallAsync(() => _api.GetTokenBalanceAsync(Chain, address, kind, tokenId, round, cancellationToken));

    /// <summary>Balance effects of an account and token, newest first (one page).</summary>
    public Task<HistoryResponse> GetHistoryAsync(string address, TokenKind kind, ulong tokenId, ulong? fromRound = null, ulong? toRound = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        CallAsync(() => { ValidateLimit(limit); return _api.GetTokenHistoryAsync(Chain, address, kind, tokenId, fromRound, toRound, cursor, limit, cancellationToken); });

    /// <summary>Holders of a token at the end of a round (one page, in account order).</summary>
    public Task<HoldersResponse> GetHoldersAsync(TokenKind kind, ulong tokenId, ulong? round = null, bool? includeZero = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        CallAsync(() => { ValidateLimit(limit); return _api.GetTokenHoldersAsync(Chain, kind, tokenId, round, includeZero, cursor, limit, cancellationToken); });

    /// <summary>Token metadata (symbol, name, decimals, standard).</summary>
    public Task<TokenResponse> GetTokenAsync(TokenKind kind, ulong tokenId, CancellationToken cancellationToken = default) =>
        CallAsync(() => _api.GetTokenAsync(Chain, kind, tokenId, cancellationToken));

    /// <summary>Starts an asynchronous export of every holder of a token. Prefer <see cref="ExportHoldersAsync"/>.</summary>
    public Task<ScanResponse> StartHolderScanAsync(TokenKind kind, ulong tokenId, ulong? round = null, bool? includeZero = null, CancellationToken cancellationToken = default) =>
        CallAsync(() => _api.CreateHolderScanAsync(Chain, kind, tokenId, new ScanRequest { Round = round, IncludeZero = includeZero }, cancellationToken));

    /// <summary>State and progress of a scan.</summary>
    public Task<ScanResponse> GetHolderScanAsync(string scanId, CancellationToken cancellationToken = default) =>
        CallAsync(() => _api.GetHolderScanAsync(Chain, scanId, cancellationToken));

    /// <summary>A result page of a scan (readable while it runs).</summary>
    public Task<ScanHoldersResponse> GetHolderScanPageAsync(string scanId, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        CallAsync(() => { ValidateLimit(limit); return _api.GetHolderScanResultAsync(Chain, scanId, cursor, limit, cancellationToken); });

    /// <summary>Cancels a scan.</summary>
    public Task<ScanResponse> CancelHolderScanAsync(string scanId, CancellationToken cancellationToken = default) =>
        CallAsync(() => _api.CancelHolderScanAsync(Chain, scanId, cancellationToken));

    /// <summary>Every holding of an account, page by page.</summary>
    public async IAsyncEnumerable<HoldingDto> EnumerateAccountTokensAsync(string address, bool? includeZero = null, int? limit = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var page = await GetAccountTokensAsync(address, includeZero, cursor, limit, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items)
            {
                yield return item;
            }

            cursor = page.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    /// <summary>Every effect of an account and token, newest first.</summary>
    public async IAsyncEnumerable<EffectDto> EnumerateHistoryAsync(string address, TokenKind kind, ulong tokenId, ulong? fromRound = null, ulong? toRound = null, int? limit = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var page = await GetHistoryAsync(address, kind, tokenId, fromRound, toRound, cursor, limit, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items)
            {
                yield return item;
            }

            cursor = page.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    /// <summary>
    /// Every holder of a token at one round. Without <paramref name="round"/> the round of the first page is pinned for the following
    /// pages, so the walk stays consistent while the indexer advances. A very large token at a past round can time out (HTTP 503):
    /// use <see cref="ExportHoldersAsync"/> for those.
    /// </summary>
    public async IAsyncEnumerable<HolderDto> EnumerateHoldersAsync(TokenKind kind, ulong tokenId, ulong? round = null, bool? includeZero = null, int? limit = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var page = await GetHoldersAsync(kind, tokenId, round, includeZero, cursor, limit, cancellationToken).ConfigureAwait(false);
            round ??= page.Round;
            foreach (var item in page.Items)
            {
                yield return item;
            }

            cursor = page.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    /// <summary>
    /// Exports every holder of a token through an asynchronous scan: starts it, waits for it and streams the result. Works for tokens of any
    /// size. Throws <see cref="HolderScanException"/> if the scan fails or is cancelled. Stopping early (break, error, cancellation) cancels the scan on the server.
    /// </summary>
    public async IAsyncEnumerable<HolderDto> ExportHoldersAsync(TokenKind kind, ulong tokenId, ulong? round = null, bool? includeZero = null, TimeSpan? pollInterval = null, int? limit = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit); // before the scan exists
        var started = await StartHolderScanAsync(kind, tokenId, round, includeZero, cancellationToken).ConfigureAwait(false);
        var scanId = started.Id.ToString();
        var wait = pollInterval ?? TimeSpan.FromSeconds(1);
        string? cursor = null;
        var finished = false;
        try
        {
            while (true)
            {
                var page = await GetHolderScanPageAsync(scanId, cursor, limit, cancellationToken).ConfigureAwait(false);

                // A finished scan has nothing left to release, whatever the consumer does with the last rows.
                finished = page.Complete;
                foreach (var item in page.Items)
                {
                    yield return item;
                }

                if (page.Complete)
                {
                    yield break;
                }

                if (!string.IsNullOrEmpty(page.NextCursor))
                {
                    cursor = page.NextCursor;
                    if (page.Items.Count > 0)
                    {
                        continue; // more rows may be waiting right now
                    }
                }

                // Caught up with a running scan (an empty page), or no row exists yet: look at the scan, then wait. The cursor is kept.
                var scan = await GetHolderScanAsync(scanId, cancellationToken).ConfigureAwait(false);
                if (scan.State != "Completed" && TerminalScanStates.Contains(scan.State))
                {
                    finished = true;
                    throw new HolderScanException($"Holder scan {scanId} ended as {scan.State}{(string.IsNullOrEmpty(scan.Error) ? string.Empty : ": " + scan.Error)}", scan);
                }

                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (!finished)
            {
                // Stopped early (break, error, cancellation): release the server-side scan, which counts against the server's limit of active scans.
                try
                {
                    using var release = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await CancelHolderScanAsync(scanId, release.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // best effort
                }
            }
        }
    }

    private static void ValidateLimit(int? limit)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "limit must be from 1 to 1000.");
        }
    }

    private async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (GeneratedApiException error)
        {
            throw ToApiException(error);
        }
        catch (HttpRequestException error)
        {
            throw new AvmBalanceIndexerConnectionException($"Request to {BaseUrl} failed: {error.Message}", error);
        }
        catch (TaskCanceledException error) when (error.InnerException is TimeoutException)
        {
            throw new AvmBalanceIndexerConnectionException($"Request to {BaseUrl} timed out.", error);
        }
    }

    /// <summary><c>Retry-After</c> is a number of seconds or an HTTP date; anything else (or an absurd value) is ignored.</summary>
    private static double? ParseRetryAfter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        double seconds;
        if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds))
        {
            if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
            {
                return null;
            }

            seconds = (date - DateTimeOffset.UtcNow).TotalSeconds;
        }

        return seconds > 0 && seconds < 1e9 ? seconds : null;
    }

    private static AvmBalanceIndexerApiException ToApiException(GeneratedApiException error)
    {
        var message = $"HTTP {error.StatusCode}";
        if (!string.IsNullOrEmpty(error.Response))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(error.Response);
                if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && document.RootElement.TryGetProperty("error", out var text)
                    && text.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    message = text.GetString() ?? message;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Not JSON (for example a 504 from a proxy).
            }
        }

        TimeSpan? retryAfter = null;
        if (error.Headers.TryGetValue("Retry-After", out var values) && ParseRetryAfter(values.FirstOrDefault()) is { } seconds)
        {
            retryAfter = TimeSpan.FromSeconds(seconds);
        }

        return new AvmBalanceIndexerApiException(error.StatusCode, message, retryAfter, error.Response, error);
    }
}
