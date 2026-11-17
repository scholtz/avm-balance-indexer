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
    public async Task The_Quota_Answer_Of_A_Public_Gateway_Becomes_Its_Own_Exception_That_Names_The_Request_And_The_Answer(bool streamed)
    {
        using var http = Client(HttpStatusCode.Forbidden, "Daily free API quota exceeded\n", streamed: streamed);

        var ex = await Assert.ThrowsAsync<NodeQuotaExceededException>(() => http.GetAsync(new Uri("v2/status", UriKind.Relative)));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Contains("/v2/status", ex.Message, StringComparison.Ordinal);
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

    private sealed class Stub(HttpStatusCode status, string body, string contentType, bool streamed) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpContent content = streamed
                ? new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body))) // a streamed body
                : new StringContent(body, Encoding.UTF8, contentType);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content, RequestMessage = request });
        }
    }
}
