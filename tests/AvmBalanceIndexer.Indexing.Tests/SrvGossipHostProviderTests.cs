using Algorand.Gossip;
using AvmBalanceIndexer.Indexing.Adapters;
using Microsoft.Extensions.Logging.Abstractions;

namespace AvmBalanceIndexer.Indexing.Tests;

public class SrvGossipHostProviderTests
{
    private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(15);

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class Resolver(Func<GossipNodePurpose, string[]> answer)
    {
        public int Archival;
        public int Relay;

        public string[] Resolve(GossipNodePurpose purpose, GossipNetwork network)
        {
            _ = purpose == GossipNodePurpose.Relay ? Interlocked.Increment(ref Relay) : Interlocked.Increment(ref Archival);
            return answer(purpose);
        }
    }

    private static SrvGossipHostProvider Provider(Resolver resolver, Clock clock) =>
        new(GossipNetwork.AlgorandTestNet, Refresh, NullLogger<SrvGossipHostProvider>.Instance, clock, resolver.Resolve);

    [Fact]
    public async Task Archival_Nodes_And_Relays_Come_From_Their_Own_Records()
    {
        using var provider = Provider(new Resolver(p => p == GossipNodePurpose.Relay ? ["http://r1"] : ["http://a1", "http://a2"]), new Clock());

        Assert.Equal(["http://a1", "http://a2"], await provider.GetHostsAsync(CancellationToken.None));
        Assert.Equal(["http://r1"], await provider.GetRelayHostsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Hosts_Are_Cached_Until_The_Refresh_Interval_Has_Passed()
    {
        var clock = new Clock();
        var resolver = new Resolver(_ => ["http://x"]);
        using var provider = Provider(resolver, clock);

        await provider.GetHostsAsync(CancellationToken.None);
        await provider.GetRelayHostsAsync(CancellationToken.None);
        clock.Advance(Refresh - TimeSpan.FromSeconds(1));
        await provider.GetHostsAsync(CancellationToken.None);
        await provider.GetRelayHostsAsync(CancellationToken.None);
        Assert.Equal((1, 1), (resolver.Archival, resolver.Relay));

        clock.Advance(TimeSpan.FromSeconds(2));
        await provider.GetHostsAsync(CancellationToken.None);
        await provider.GetRelayHostsAsync(CancellationToken.None);
        Assert.Equal((2, 2), (resolver.Archival, resolver.Relay));
    }

    [Fact]
    public async Task A_Network_Without_Relays_Gives_An_Empty_List_Without_Breaking_The_Archival_Nodes()
    {
        var resolver = new Resolver(p => p == GossipNodePurpose.Relay ? [] : ["http://a1"]);
        using var provider = Provider(resolver, new Clock());

        Assert.Empty(await provider.GetRelayHostsAsync(CancellationToken.None));
        Assert.Equal(["http://a1"], await provider.GetHostsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_Failed_Relay_Lookup_Is_Not_Repeated_Every_Few_Seconds()
    {
        var clock = new Clock();
        var resolver = new Resolver(p => p == GossipNodePurpose.Relay ? throw new InvalidOperationException("no SRV record") : ["http://a1"]);
        using var provider = Provider(resolver, clock);

        await provider.GetRelayHostsAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(await provider.GetRelayHostsAsync(CancellationToken.None));

        Assert.Equal(1, resolver.Relay);
        clock.Advance(Refresh);
        await provider.GetRelayHostsAsync(CancellationToken.None);
        Assert.Equal(2, resolver.Relay);
    }

    [Fact]
    public async Task A_Failed_Archival_Lookup_Is_Retried_Soon_And_Reported()
    {
        var clock = new Clock();
        var resolver = new Resolver(_ => throw new InvalidOperationException("dns down"));
        using var provider = Provider(resolver, clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetHostsAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromSeconds(6));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetHostsAsync(CancellationToken.None));

        Assert.Equal(2, resolver.Archival);
    }

    [Fact]
    public async Task A_Failed_Refresh_Keeps_Serving_The_Last_Known_List_Of_That_Kind_Only()
    {
        var clock = new Clock();
        var fail = false;
        var resolver = new Resolver(p => fail ? throw new InvalidOperationException("dns down") : [p == GossipNodePurpose.Relay ? "http://r1" : "http://a1"]);
        using var provider = Provider(resolver, clock);
        await provider.GetHostsAsync(CancellationToken.None);
        await provider.GetRelayHostsAsync(CancellationToken.None);

        fail = true;
        clock.Advance(Refresh + TimeSpan.FromSeconds(1));

        Assert.Equal(["http://a1"], await provider.GetHostsAsync(CancellationToken.None));
        Assert.Equal(["http://r1"], await provider.GetRelayHostsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Asking_After_Dispose_Is_A_Cancellation_Not_A_Failure()
    {
        var provider = Provider(new Resolver(_ => ["http://x"]), new Clock());
        provider.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetHostsAsync(CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRelayHostsAsync(CancellationToken.None));
    }
}
