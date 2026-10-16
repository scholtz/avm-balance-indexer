using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Tests;

public class HostGateTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    private static HostGate Gate(int max = 32, int maxDelaySeconds = 60) => new(max, TimeSpan.FromSeconds(maxDelaySeconds));

    [Fact]
    public void Admits_Up_To_The_Limit_And_Frees_A_Slot_On_Exit()
    {
        var gate = Gate(max: 2);

        Assert.True(gate.TryEnter(Start));
        Assert.True(gate.TryEnter(Start));
        Assert.False(gate.TryEnter(Start));

        gate.Exit();

        Assert.True(gate.TryEnter(Start));
    }

    [Fact]
    public void A_Throttle_Pauses_The_Host_For_The_Retry_After_Value_And_Halves_The_Limit()
    {
        var gate = Gate();

        var result = gate.OnThrottled(TimeSpan.FromSeconds(7), Start);

        Assert.True(result.NewPause);
        Assert.Equal(TimeSpan.FromSeconds(7), result.Delay);
        Assert.Equal(16, gate.Limit);
        Assert.False(gate.TryEnter(Start.AddSeconds(6.9)));
        Assert.True(gate.TryEnter(Start.AddSeconds(7)));
    }

    [Fact]
    public void The_Pause_Is_Capped()
    {
        var gate = Gate(maxDelaySeconds: 60);

        Assert.Equal(TimeSpan.FromSeconds(60), gate.OnThrottled(TimeSpan.FromHours(1), Start).Delay);
    }

    [Fact]
    public void Without_Retry_After_The_Pause_Doubles_With_Every_Consecutive_Pause_Up_To_The_Cap()
    {
        var gate = Gate(maxDelaySeconds: 5);
        var now = Start;
        var delays = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var result = gate.OnThrottled(null, now);
            delays.Add(result.Delay.TotalSeconds);
            now = now.Add(result.Delay); // the next throttle comes after the pause ended
        }

        Assert.Equal([1d, 2d, 4d, 5d, 5d], delays);
    }

    [Fact]
    public void Responses_That_Were_Already_In_Flight_When_The_Pause_Began_Do_Not_Punish_The_Host_Again()
    {
        var gate = Gate();
        gate.OnThrottled(null, Start);

        var late = gate.OnThrottled(null, Start.AddMilliseconds(50));

        Assert.False(late.NewPause);
        Assert.Equal(16, gate.Limit);
        Assert.Equal(Start.AddSeconds(1), gate.BlockedUntil);
    }

    [Fact]
    public void A_Longer_Retry_After_From_A_Late_Response_Extends_The_Pause_Without_Halving_Again()
    {
        var gate = Gate();
        gate.OnThrottled(null, Start);

        gate.OnThrottled(TimeSpan.FromSeconds(10), Start.AddMilliseconds(50));

        Assert.Equal(Start.AddMilliseconds(50).AddSeconds(10), gate.BlockedUntil);
        Assert.Equal(16, gate.Limit);
    }

    [Fact]
    public void The_Limit_Never_Drops_Below_One()
    {
        var gate = Gate(max: 4);
        var now = Start;
        for (var i = 0; i < 8; i++)
        {
            now = now.Add(gate.OnThrottled(TimeSpan.FromSeconds(1), now).Delay);
        }

        Assert.Equal(1, gate.Limit);
    }

    [Fact]
    public void Successes_Grow_The_Limit_Back_By_One_Per_Limit_Worth_Of_Requests_Up_To_The_Maximum()
    {
        var gate = Gate(max: 8);
        var now = Start;
        now = now.Add(gate.OnThrottled(TimeSpan.FromSeconds(1), now).Delay); // limit 4

        for (var i = 0; i < 10; i++)
        {
            gate.OnSuccess(now);
        }

        Assert.InRange(gate.Limit, 5, 6); // roughly one more request per limit's worth of successes

        for (var i = 0; i < 1_000; i++)
        {
            gate.OnSuccess(now);
        }

        Assert.Equal(8, gate.Limit);
    }

    [Fact]
    public void A_Success_Ends_The_Run_Of_Consecutive_Pauses()
    {
        var gate = Gate(maxDelaySeconds: 60);
        var now = Start;
        now = now.Add(gate.OnThrottled(null, now).Delay);
        now = now.Add(gate.OnThrottled(null, now).Delay);

        gate.OnSuccess(now);

        Assert.Equal(TimeSpan.FromSeconds(1), gate.OnThrottled(null, now).Delay);
    }
}
