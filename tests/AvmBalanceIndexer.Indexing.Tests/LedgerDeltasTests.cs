using System.Net;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Indexing.Adapters;
using Microsoft.Extensions.Logging.Abstractions;

namespace AvmBalanceIndexer.Indexing.Tests;

public class LedgerDeltasTests
{
    private static Address Account(byte b) => new([.. Enumerable.Repeat(b, 32)]);

    private static string Base64(byte b) => Convert.ToBase64String([.. Enumerable.Repeat(b, 32)]);

    [Fact]
    public void A_Real_Testnet_Document_With_Integer_Keys_After_Accts_Is_Parsed()
    {
        // Round 69341216 of TestNet, captured from the public algod: four accounts, and a tail with an integer map key (invalid JSON).
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "testnet-69341216-deltas.json"));

        var delta = LedgerDelta.Parse(text);

        Assert.Equal(4, delta.Algo.Count);
        Assert.Equal(2_092_118_321UL, delta.Algo[Account(0xFF)]);
        Assert.Equal(328_826_213_000_815UL, delta.Algo[new Address(Convert.FromBase64String("PS8CY4IXBUj2tmoZIrFw6LgsdUIB5tSUErJEo0T80Ic="))]);
        Assert.Empty(delta.Assets);
    }

    [Fact]
    public void Asset_Holdings_Are_Read_A_Removed_Holding_Is_Null_And_A_Zero_Holding_Is_Zero()
    {
        var text = $$$$"""
            {"Accts":{"Accts":[{"Addr":"{{{{Base64(1)}}}}","MicroAlgos":5000}],
              "AssetResources":[
                {"Addr":"{{{{Base64(1)}}}}","Aidx":31566704,"Holding":{"Deleted":false,"Holding":{"a":77,"f":false}}},
                {"Addr":"{{{{Base64(2)}}}}","Aidx":31566704,"Holding":{"Deleted":true,"Holding":{}}},
                {"Addr":"{{{{Base64(3)}}}}","Aidx":31566704,"Holding":{"Deleted":false,"Holding":{}}},
                {"Addr":"{{{{Base64(4)}}}}","Aidx":9,"Params":{"Deleted":false},"Holding":{"Deleted":false}}
              ]},
             "KvMods":{7:{"Data":"AA=="}}}
            """;

        var delta = LedgerDelta.Parse(text);

        Assert.Equal(5000UL, delta.Algo[Account(1)]);
        Assert.Equal(77UL, delta.Assets[(Account(1), 31566704)]);
        Assert.Null(delta.Assets[(Account(2), 31566704)]);
        Assert.Equal(0UL, delta.Assets[(Account(3), 31566704)]);
        Assert.False(delta.Assets.ContainsKey((Account(4), 9))); // no holding information in this entry (a created asset's parameters only)
    }

    [Fact]
    public void A_Document_Without_Accts_Is_Refused()
    {
        Assert.Throws<InvalidDataException>(() => LedgerDelta.Parse("{\"Totals\":{}}"));
        Assert.Throws<InvalidDataException>(() => LedgerDelta.Parse("{\"Accts\":{\"Accts\":["));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task A_Node_Without_Deltas_Gives_Null(HttpStatusCode status)
    {
        using var source = Source(_ => new HttpResponseMessage(status));

        Assert.Null(await source.TryGetAsync(5, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task A_Node_That_Does_Not_Answer_Is_An_Error_Not_A_Statement_About_The_Round(HttpStatusCode status)
    {
        using var source = Source(_ => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<HttpRequestException>(() => source.TryGetAsync(5, CancellationToken.None));
    }

    [Fact]
    public async Task An_Unreadable_Document_Is_An_Error_And_A_Good_One_Is_Read_From_The_Right_Url()
    {
        var requested = new List<string>();
        using var source = Source(request =>
        {
            requested.Add(request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(requested.Count == 1 ? "not json" : $$$"""{"Accts":{"Accts":[{"Addr":"{{{Base64(9)}}}","MicroAlgos":1}]}}""") };
        });

        await Assert.ThrowsAsync<InvalidDataException>(() => source.TryGetAsync(5, CancellationToken.None));
        var delta = await source.TryGetAsync(6, CancellationToken.None);

        Assert.Equal(1UL, delta!.Algo[Account(9)]);
        Assert.Equal(["http://algod.test/v2/deltas/5", "http://algod.test/v2/deltas/6"], requested);
    }

    private static AlgodLedgerDeltaSource Source(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new Handler(respond)) { BaseAddress = new Uri("http://algod.test/") }, NullLogger<AlgodLedgerDeltaSource>.Instance, ownsClient: true);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
