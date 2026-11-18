using System.Net;
using System.Text;
using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Tests;

public class AlgodQuotaHandlerTests
{
    private static HttpClient Client(HttpStatusCode status, string body, string contentType = "text/plain", bool streamed = false) =>
        new(new AlgodQuotaHandler(new Stub(status, body, contentType, streamed))) { BaseAddress = new Uri("https://node.example/") };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Quota_Answer_Of_A_Public_Gateway_Becomes_Its_Own_Exception_That_Names_The_Answer_But_Not_The_Path(bool streamed)
    {
        using var http = Client(HttpStatusCode.Forbidden, "Daily free API quota exceeded\n", streamed: streamed);

        var ex = await Assert.ThrowsAsync<NodeQuotaExceededException>(() => http.GetAsync(new Uri("secret-key/v2/status", UriKind.Relative)));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.DoesNotContain("secret-key", ex.Message, StringComparison.Ordinal); // some gateways carry the API key in the path
        Assert.Contains("Daily free API quota exceeded", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden")] // a blocked address or a bad token: still a failure
    [InlineData(HttpStatusCode.Forbidden, "")]
    [InlineData(HttpStatusCode.TooManyRequests, "quota per second exceeded")] // throttling has its own handling
    [InlineData(HttpStatusCode.OK, "{\"note\":\"quota\"}")]
    public async Task Any_Other_Answer_Is_Passed_On_With_Its_Body(HttpStatusCode status, string body)
    {
        using var http = Client(status, body);

        using var response = await http.GetAsync(new Uri("v2/status", UriKind.Relative));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_Long_403_Body_Is_Not_Read_As_A_Quota_Answer()
    {
        var page = "<html>" + new string('x', 2000) + " quota </html>";
        using var http = Client(HttpStatusCode.Forbidden, page, "text/html");

        using var response = await http.GetAsync(new Uri("v2/status", UriKind.Relative));

        Assert.Equal(page, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_Long_403_Body_Without_A_Length_Is_Read_Up_To_The_Limit_Only_And_Is_No_Quota_Answer()
    {
        var page = "<html>" + new string('x', 5000) + " quota </html>";
        using var http = Client(HttpStatusCode.Forbidden, page, "text/html", streamed: true);

        using var response = await http.GetAsync(new Uri("v2/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(page[..1025], await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_403_Body_That_Cannot_Be_Read_Is_A_Failed_Request()
    {
        using var http = new HttpClient(new AlgodQuotaHandler(new Broken())) { BaseAddress = new Uri("https://node.example/") };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(new Uri("v2/status", UriKind.Relative)));

        Assert.IsNotType<NodeQuotaExceededException>(ex);
        Assert.IsType<IOException>(ex.InnerException);
    }

    /// <summary>The clients the factory builds carry the handler: a real request to a server that names its quota.</summary>
    [Fact]
    public async Task The_Algod_Clients_Of_The_Factory_Report_The_Quota_Answer()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var request = new byte[4096];
            _ = await stream.ReadAsync(request);
            const string Body = "Daily free API quota exceeded";
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 403 Forbidden\r\nContent-Type: text/plain\r\nContent-Length: {Body.Length}\r\nConnection: close\r\n\r\n{Body}"));
        });
        var options = new AlgorandChainOptions { Chain = Domain.Chains.ChainId.AlgorandTestnet, AlgodUrl = $"http://127.0.0.1:{port}" };
        using var deltas = AlgorandChainFactory.CreateLedgerDeltaSource(options, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        await Assert.ThrowsAsync<NodeQuotaExceededException>(() => deltas.TryGetAsync(7, CancellationToken.None));
        await server;
    }

    private sealed class Broken : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StreamContent(new Resetting()), RequestMessage = request });
    }

    private sealed class Resetting() : MemoryStream(new byte[8])
    {
        public override bool CanSeek => false;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("Connection reset by peer");
    }

    private sealed class NoLength(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class Stub(HttpStatusCode status, string body, string contentType, bool streamed) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpContent content = streamed
                ? new StreamContent(new NoLength(Encoding.UTF8.GetBytes(body))) // no Content-Length, as a chunked or decompressed answer
                : new StringContent(body, Encoding.UTF8, contentType);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content, RequestMessage = request });
        }
    }
}
