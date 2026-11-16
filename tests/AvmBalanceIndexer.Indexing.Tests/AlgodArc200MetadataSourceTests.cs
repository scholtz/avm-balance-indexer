using System.Net;
using System.Text;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;

namespace AvmBalanceIndexer.Indexing.Tests;

public class AlgodArc200MetadataSourceTests
{
    private static readonly byte[] Sender = Enumerable.Range(1, 32).Select(static i => (byte)i).ToArray();
    private static readonly byte[] Genesis = Enumerable.Range(100, 32).Select(static i => (byte)i).ToArray();
    private static readonly byte[] Prefix = [0x15, 0x1f, 0x7c, 0x75];

    private static string Logs(params byte[][] parts) => ResultJson(Convert.ToBase64String([.. Prefix, .. parts.SelectMany(static p => p)]));

    private static string ResultJson(string logBase64) =>
        "{\"txn-groups\":[{\"txn-results\":[{\"txn-result\":{\"logs\":[\"" + Convert.ToBase64String([1, 2]) + "\",\"" + logBase64 + "\"]}}]}],\"version\":2}";

    private static string Failed => """{"txn-groups":[{"failure-message":"logic eval error","txn-results":[{"txn-result":{}}]}],"version":2}""";

    private static byte[] Padded(string text, int size)
    {
        var bytes = new byte[size];
        Encoding.UTF8.GetBytes(text).CopyTo(bytes, 0);
        return bytes;
    }

    private static string Answer(byte[] selector) => Convert.ToHexStringLower(selector) switch
    {
        "657d13ec" => Logs(Padded("Voi Token", 32)),
        "b6ae1a25" => Logs(Padded("VOI", 8)),
        "84ec13d5" => Logs([6]),
        "ec996041" => Logs([.. new byte[24], 0, 0, 0, 0, 0, 0, 1, 0]),
        _ => throw new InvalidOperationException("unexpected selector"),
    };

    private static byte[] SelectorOf(byte[] request)
    {
        var map = MessagePackSerializer.Deserialize<Dictionary<string, object>>(request);
        var groups = (object[])map["txn-groups"];
        var txns = (object[])((Dictionary<object, object>)groups[0])["txns"];
        var txn = (Dictionary<object, object>)((Dictionary<object, object>)txns[0])["txn"];
        return (byte[])((object[])txn["apaa"])[0];
    }

    private sealed class Handler(Func<byte[], HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<byte[]> Requests { get; } = [];

        public List<Uri?> Uris { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            lock (Requests)
            {
                Requests.Add(body);
                Uris.Add(request.RequestUri);
            }

            return respond(body);
        }
    }

    private static AlgodArc200MetadataSource Source(Handler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://algod.test/") }, Genesis, NullLogger<AlgodArc200MetadataSource>.Instance);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task A_Full_Answer_Gives_A_Complete_Record()
    {
        var handler = new Handler(request => Json(Answer(SelectorOf(request))));

        var metadata = await Source(handler).TryReadAsync(42, Sender, 1000, CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal(("Voi Token", "VOI", 6, 256), (metadata.Name, metadata.Symbol, metadata.Decimals, (int)metadata.TotalSupply!.Value));
        Assert.True(metadata.Complete);
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Uris, uri => Assert.Equal("/v2/transactions/simulate", uri!.AbsolutePath));
    }

    [Fact]
    public async Task The_Request_Is_A_Canonical_Msgpack_Simulate_Call()
    {
        var handler = new Handler(request => Json(Answer(SelectorOf(request))));
        await Source(handler).TryReadAsync(42, Sender, 1000, CancellationToken.None);

        var body = handler.Requests.First(request => SelectorOf(request).AsSpan().SequenceEqual(Arc200Metadata.Selector("arc200_decimals()uint8")));
        var map = MessagePackSerializer.Deserialize<Dictionary<string, object>>(body);
        Assert.Equal(["allow-empty-signatures", "allow-unnamed-resources", "txn-groups"], map.Keys);
        Assert.True((bool)map["allow-empty-signatures"]);
        Assert.True((bool)map["allow-unnamed-resources"]);

        var txn = (Dictionary<object, object>)((Dictionary<object, object>)((object[])((Dictionary<object, object>)((object[])map["txn-groups"])[0])["txns"])[0])["txn"];
        Assert.Equal(["apaa", "apid", "fee", "fv", "gh", "lv", "snd", "type"], txn.Keys.Cast<string>());
        Assert.Equal(42UL, Convert.ToUInt64(txn["apid"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1000UL, Convert.ToUInt64(txn["fee"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1000UL, Convert.ToUInt64(txn["fv"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(2000UL, Convert.ToUInt64(txn["lv"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(Genesis, (byte[])txn["gh"]);
        Assert.Equal(Sender, (byte[])txn["snd"]);
        Assert.Equal("appl", (string)txn["type"]);
    }

    [Fact]
    public async Task A_Failing_Method_Leaves_Only_That_Field_Empty()
    {
        var handler = new Handler(request =>
            SelectorOf(request).AsSpan().SequenceEqual(Arc200Metadata.Selector("arc200_symbol()byte[8]")) ? Json(Failed) : Json(Answer(SelectorOf(request))));

        var metadata = await Source(handler).TryReadAsync(42, Sender, 1000, CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal(("Voi Token", null, 6), (metadata.Name, metadata.Symbol, metadata.Decimals));
        Assert.False(metadata.Complete);
    }

    [Fact]
    public async Task An_Application_Without_The_Methods_Gives_An_Empty_Record()
    {
        var handler = new Handler(_ => Json(Failed));

        var metadata = await Source(handler).TryReadAsync(42, Sender, 1000, CancellationToken.None);

        Assert.Equal(new Arc200Metadata(null, null, null, null), metadata);
    }

    [Fact]
    public async Task An_Http_Error_Or_Malformed_Answer_Means_The_Node_Could_Not_Be_Asked()
    {
        foreach (var respond in new Func<byte[], HttpResponseMessage>[]
        {
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            _ => Json("not json"),
            _ => Json("""{"txn-groups":[]}"""),
        })
        {
            Assert.Null(await Source(new Handler(respond)).TryReadAsync(42, Sender, 1000, CancellationToken.None));
        }
    }

    [Theory]
    [InlineData("http://gw.test/algod", "http://gw.test/algod/v2/transactions/simulate")]
    [InlineData("http://gw.test/algod/", "http://gw.test/algod/v2/transactions/simulate")]
    [InlineData("http://gw.test", "http://gw.test/v2/transactions/simulate")]
    public void A_Gateway_Path_Without_A_Trailing_Slash_Keeps_Its_Last_Segment(string configured, string expected) =>
        Assert.Equal(expected, new Uri(AlgorandChainFactory.WithTrailingSlash(new Uri(configured)), "v2/transactions/simulate").AbsoluteUri);

    [Fact]
    public async Task A_Hanging_Node_Is_Given_Up_On_After_The_Timeout()
    {
        var source = new AlgodArc200MetadataSource(
            new HttpClient(new HangingHandler()) { BaseAddress = new Uri("http://algod.test/") }, Genesis, NullLogger<AlgodArc200MetadataSource>.Instance);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // The caller's cancellation is not swallowed: it is the shutdown path.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.TryReadAsync(42, Sender, 1000, cancel.Token));
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }
}
