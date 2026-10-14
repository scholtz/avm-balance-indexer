using System.Net;
using System.Text;
using System.Text.Json;
using AvmBalanceIndexer.Client.Generated;
using Xunit;

namespace AvmBalanceIndexer.Client.Tests;

public sealed class ClientTests
{
    private sealed record Seen(HttpMethod Method, Uri Uri, string Body, System.Net.Http.Headers.HttpRequestHeaders Headers);

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Seen(request.Method, request.RequestUri!, body, request.Headers));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    private static AvmBalanceIndexerClient Client(FakeHandler handler, ChainId chain = ChainId.AlgorandMainnet, Uri? baseUrl = null)
    {
        var options = new AvmBalanceIndexerOptions { Chain = chain, BaseUrl = baseUrl ?? new Uri("http://indexer.test"), HttpClient = new HttpClient(handler) };
        return new AvmBalanceIndexerClient(options);
    }

    private static object Holder(string account) => new { account, amount = "5", optedIn = true, lastChangedRound = 7 };

    private static object Scan(string state, bool complete, string? error = null) => new
    {
        id = "11111111-1111-1111-1111-111111111111",
        state,
        token = new { kind = "asa", tokenId = 1 },
        round = 5,
        includeZero = false,
        holdersFound = 2,
        complete,
        error,
        createdAt = "2026-10-14T10:00:00Z",
        updatedAt = "2026-10-14T10:00:00Z",
        expiresAt = "2026-10-15T10:00:00Z",
    };

    [Fact]
    public void Defaults_Are_The_Public_Deployments()
    {
        using var testnet = AvmBalanceIndexerClient.AlgorandTestnet();
        using var mainnet = AvmBalanceIndexerClient.AlgorandMainnet();
        using var voi = AvmBalanceIndexerClient.Voi();
        using var unspecified = new AvmBalanceIndexerClient();

        Assert.Equal(new Uri("https://testnet.balance-indexer.biatec.io"), testnet.BaseUrl);
        Assert.Equal(new Uri("https://mainnet.balance-indexer.biatec.io"), mainnet.BaseUrl);
        Assert.Equal(new Uri("https://voi.balance-indexer.biatec.io"), voi.BaseUrl);
        Assert.Equal(ChainId.AlgorandMainnet, unspecified.Chain);
    }

    [Fact]
    public async Task A_Custom_Base_Url_Is_Used_With_Its_Path_Prefix()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, new { chain = "voi", provider = "Cassandra", state = "Following", indexedRound = 10, targetRound = 11, lag = 1, updatedAt = "2026-10-14T10:00:00Z" }));
        using var client = Client(handler, ChainId.Voi, new Uri("https://my.example.com/indexer/"));

        var status = await client.GetStatusAsync();

        Assert.Equal(10UL, status.IndexedRound);
        Assert.Equal("https://my.example.com/indexer/v1/voi/status", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task Headers_And_User_Agent_Are_Sent()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, new { chain = "voi", provider = "x", state = "x", indexedRound = (int?)null, targetRound = (int?)null, lag = (int?)null, updatedAt = (string?)null }));
        var options = new AvmBalanceIndexerOptions { BaseUrl = new Uri("http://indexer.test"), HttpClient = new HttpClient(handler) };
        options.Headers["X-Api-Key"] = "k";
        using var client = new AvmBalanceIndexerClient(options);

        await client.GetStatusAsync();

        Assert.Equal("k", handler.Requests[0].Headers.GetValues("X-Api-Key").Single());
        Assert.Contains("avm-balance-indexer", handler.Requests[0].Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task A_Page_Size_Outside_1_To_1000_Is_Rejected_Before_Sending()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, new { }));
        using var client = Client(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetHoldersAsync(TokenKind.Asa, 1, limit: 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetAccountTokensAsync("A", limit: 5000));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void A_Header_The_Framework_Refuses_Fails_At_Construction()
    {
        var options = new AvmBalanceIndexerOptions { BaseUrl = new Uri("http://indexer.test") };
        options.Headers["bad header name"] = "x";

        Assert.Throws<ArgumentException>(() => new AvmBalanceIndexerClient(options));
    }

    [Fact]
    public async Task Query_Parameters_And_Enum_Values_Are_Serialised()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, new { token = new { kind = "asa", tokenId = 31566704 }, round = 9, indexedRound = 9, items = Array.Empty<object>(), nextCursor = (string?)null }));
        using var client = Client(handler);

        await client.GetHoldersAsync(TokenKind.Asa, 31566704, round: 9, includeZero: true, cursor: "abc", limit: 50);

        var uri = handler.Requests[0].Uri;
        Assert.Equal("/v1/algorand-mainnet/tokens/asa/31566704/holders", uri.AbsolutePath);
        Assert.Equal("?round=9&includeZero=true&cursor=abc&limit=50", uri.Query);
    }

    [Fact]
    public async Task A_Scan_Is_Posted()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.Accepted, Scan("Pending", false)));
        using var client = Client(handler);

        var scan = await client.StartHolderScanAsync(TokenKind.Asa, 1, round: 5);

        Assert.Equal("Pending", scan.State);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(5, body.RootElement.GetProperty("round").GetInt32());
    }

    [Fact]
    public async Task An_Error_Status_Becomes_An_Api_Exception()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.TooManyRequests, new { error = "slow down" }, ("Retry-After", "7")));
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<AvmBalanceIndexerApiException>(() => client.GetStatusAsync());

        Assert.Equal(429, error.StatusCode);
        Assert.Equal("slow down", error.ApiMessage);
        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public async Task Retry_After_May_Be_An_Http_Date_And_Nonsense_Is_Ignored()
    {
        async Task<TimeSpan?> RetryAfter(string header)
        {
            var handler = new FakeHandler(_ => Json(HttpStatusCode.TooManyRequests, new { error = "slow down" }, ("Retry-After", header)));
            using var client = Client(handler);
            var error = await Assert.ThrowsAsync<AvmBalanceIndexerApiException>(() => client.GetStatusAsync());
            return error.RetryAfter;
        }

        var inThirtySeconds = await RetryAfter(DateTimeOffset.UtcNow.AddSeconds(30).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotNull(inThirtySeconds);
        Assert.InRange(inThirtySeconds.Value.TotalSeconds, 1, 30);
        Assert.Null(await RetryAfter("Infinity"));
        Assert.Null(await RetryAfter("soon"));
    }

    [Fact]
    public async Task A_Success_Answer_That_Is_Not_Json_Is_A_Response_Exception()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>captive portal</html>", Encoding.UTF8, "text/html") });
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<AvmBalanceIndexerResponseException>(() => client.GetStatusAsync());

        Assert.Equal(200, error.StatusCode);
        Assert.Contains("captive portal", error.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_Found_With_A_Plain_Body_Keeps_The_Body()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("nope") });
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<AvmBalanceIndexerApiException>(() => client.GetTokenAsync(TokenKind.Asa, 1));

        Assert.True(error.IsNotFound);
        Assert.Equal("nope", error.Body);
    }

    [Fact]
    public async Task A_Network_Failure_Becomes_A_Connection_Exception()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("refused"));
        using var client = Client(handler);

        await Assert.ThrowsAsync<AvmBalanceIndexerConnectionException>(() => client.GetStatusAsync());
    }

    [Fact]
    public async Task Holder_Pages_Are_Walked_With_The_Round_Pinned()
    {
        var handler = new FakeHandler(request =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            object Page(string account, string? next) => new { token = new { kind = "asa", tokenId = 1 }, round = 42, indexedRound = 50, items = new[] { Holder(account) }, nextCursor = next };
            return query["cursor"] switch
            {
                null => Json(HttpStatusCode.OK, Page("A", "c1")),
                "c1" => Json(HttpStatusCode.OK, Page("B", "c2")),
                _ => Json(HttpStatusCode.OK, Page("C", null)),
            };
        });
        using var client = Client(handler);

        var accounts = new List<string>();
        await foreach (var holder in client.EnumerateHoldersAsync(TokenKind.Asa, 1))
        {
            accounts.Add(holder.Account);
        }

        Assert.Equal(["A", "B", "C"], accounts);
        Assert.DoesNotContain("round=", handler.Requests[0].Uri.Query);
        Assert.Contains("round=42", handler.Requests[1].Uri.Query);
        Assert.Contains("round=42", handler.Requests[2].Uri.Query);
    }

    [Fact]
    public async Task An_Export_Waits_For_The_Scan_And_Keeps_Its_Cursor()
    {
        var waited = false;
        const string id = "11111111-1111-1111-1111-111111111111";
        var handler = new FakeHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Accepted, Scan("Pending", false));
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/holders", StringComparison.Ordinal))
            {
                var cursor = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["cursor"];
                if (cursor is null)
                {
                    return Json(HttpStatusCode.OK, new { id, state = "Running", complete = false, round = 5, items = new[] { Holder("A") }, nextCursor = "c1" });
                }

                if (!waited)
                {
                    // Caught up with a running scan: no new rows, and the server answers with the position to continue from.
                    waited = true;
                    return Json(HttpStatusCode.OK, new { id, state = "Running", complete = false, round = 5, items = Array.Empty<object>(), nextCursor = "c1" });
                }

                return Json(HttpStatusCode.OK, new { id, state = "Completed", complete = true, round = 5, items = new[] { Holder("B") }, nextCursor = (string?)null });
            }

            return Json(HttpStatusCode.OK, Scan("Running", false));
        });
        using var client = Client(handler);

        var accounts = new List<string>();
        await foreach (var holder in client.ExportHoldersAsync(TokenKind.Asa, 1, pollInterval: TimeSpan.FromMilliseconds(10)))
        {
            accounts.Add(holder.Account);
        }

        Assert.Equal(["A", "B"], accounts);
        var pageQueries = handler.Requests.Where(r => r.Uri.AbsolutePath.EndsWith("/holders", StringComparison.Ordinal)).Select(r => r.Uri.Query).ToArray();
        Assert.DoesNotContain("cursor", pageQueries[0], StringComparison.Ordinal);
        Assert.Contains("cursor=c1", pageQueries[1], StringComparison.Ordinal);
        Assert.Contains("cursor=c1", pageQueries[2], StringComparison.Ordinal);

        // Caught up with a running scan: it looks at the scan and waits instead of asking for the next page at once.
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Get && r.Uri.AbsolutePath.EndsWith("/scans/" + id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_Last_Page_Of_A_Scan_That_Finished_Between_Two_Server_Reads_Is_Not_Read_Twice()
    {
        var handler = new FakeHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Accepted, Scan("Pending", false));
            }

            // Rows, no cursor and not complete: the job looked running when the endpoint read it and was finished when the store did.
            return request.RequestUri!.AbsolutePath.EndsWith("/holders", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, new { id = "11111111-1111-1111-1111-111111111111", state = "Running", complete = false, round = 5, items = new[] { Holder("A"), Holder("B") }, nextCursor = (string?)null })
                : Json(HttpStatusCode.OK, Scan("Completed", true));
        });
        using var client = Client(handler);

        var accounts = new List<string>();
        await foreach (var holder in client.ExportHoldersAsync(TokenKind.Asa, 1, pollInterval: TimeSpan.FromMilliseconds(10)))
        {
            accounts.Add(holder.Account);
        }

        Assert.Equal(["A", "B"], accounts);
    }

    [Fact]
    public async Task An_Export_Validates_The_Page_Size_Before_It_Starts_A_Scan()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, new { }));
        using var client = Client(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var _ in client.ExportHoldersAsync(TokenKind.Asa, 1, limit: 0))
            {
            }
        });
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Stopping_Inside_The_Last_Page_Does_Not_Cancel_A_Finished_Scan()
    {
        var handler = new FakeHandler(request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Accepted, Scan("Pending", false))
            : Json(HttpStatusCode.OK, new { id = "11111111-1111-1111-1111-111111111111", state = "Completed", complete = true, round = 5, items = new[] { Holder("A"), Holder("B") }, nextCursor = (string?)null }));
        using var client = Client(handler);

        await foreach (var _ in client.ExportHoldersAsync(TokenKind.Asa, 1))
        {
            break;
        }

        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task Stopping_An_Export_Early_Cancels_The_Scan()
    {
        var handler = new FakeHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Accepted, Scan("Pending", false));
            }

            if (request.Method == HttpMethod.Delete)
            {
                return Json(HttpStatusCode.OK, Scan("Cancelled", false));
            }

            return Json(HttpStatusCode.OK, new { id = "11111111-1111-1111-1111-111111111111", state = "Running", complete = false, round = 5, items = new[] { Holder("A"), Holder("B") }, nextCursor = "c1" });
        });
        using var client = Client(handler);

        await foreach (var _ in client.ExportHoldersAsync(TokenKind.Asa, 1, pollInterval: TimeSpan.FromMilliseconds(10)))
        {
            break;
        }

        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task A_Callers_Http_Client_Is_Not_Modified()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, new { chain = "voi", provider = "x", state = "x", indexedRound = (int?)null, targetRound = (int?)null, lag = (int?)null, updatedAt = (string?)null }));
        using var shared = new HttpClient(handler);
        var options = new AvmBalanceIndexerOptions { Chain = ChainId.Voi, BaseUrl = new Uri("http://indexer.test/prefix"), HttpClient = shared };
        options.Headers["X-Api-Key"] = "k";
        using var client = new AvmBalanceIndexerClient(options);

        await client.GetStatusAsync();
        await client.GetStatusAsync(); // a second request must not trip over a changed BaseAddress either

        Assert.Null(shared.BaseAddress);
        Assert.Empty(shared.DefaultRequestHeaders);
        Assert.Equal("http://indexer.test/prefix/v1/voi/status", handler.Requests[0].Uri.ToString());
        Assert.Equal("k", handler.Requests[0].Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public void The_Factory_Methods_Do_Not_Change_The_Callers_Options()
    {
        var options = new AvmBalanceIndexerOptions { Chain = ChainId.AlgorandMainnet };
        options.Headers["X-Api-Key"] = "k";

        using var voi = AvmBalanceIndexerClient.Voi(options);
        using var testnet = AvmBalanceIndexerClient.AlgorandTestnet(options);

        Assert.Equal(ChainId.AlgorandMainnet, options.Chain);
        Assert.Equal(ChainId.Voi, voi.Chain);
        Assert.Equal(ChainId.AlgorandTestnet, testnet.Chain);
        Assert.Equal(new Uri("https://voi.balance-indexer.biatec.io"), voi.BaseUrl);
    }

    [Fact]
    public async Task An_Export_Of_A_Failed_Scan_Throws()
    {
        var handler = new FakeHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Accepted, Scan("Failed", false, "boom"));
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/holders", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, new { id = "11111111-1111-1111-1111-111111111111", state = "Failed", complete = false, round = 5, items = Array.Empty<object>(), nextCursor = (string?)null });
            }

            return Json(HttpStatusCode.OK, Scan("Failed", false, "boom"));
        });
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<HolderScanException>(async () =>
        {
            await foreach (var _ in client.ExportHoldersAsync(TokenKind.Asa, 1, pollInterval: TimeSpan.FromMilliseconds(10)))
            {
            }
        });

        Assert.Equal("Failed", error.Scan.State);
        Assert.Contains("boom", error.Message, StringComparison.Ordinal);
    }
}
