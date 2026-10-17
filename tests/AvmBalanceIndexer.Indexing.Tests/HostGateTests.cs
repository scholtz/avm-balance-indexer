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

        var result = gate.OnThrottled(TimeSpan.FromSeconds(7), Start, Start);

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

        Assert.Equal(TimeSpan.FromSeconds(60), gate.OnThrottled(TimeSpan.FromHours(1), Start, Start).Delay);
    }

    [Fact]
    public void Without_Retry_After_The_Pause_Doubles_With_Every_Consecutive_Pause_Up_To_The_Cap()
    {
        var gate = Gate(maxDelaySeconds: 5);
        var now = Start;
        var delays = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var result = gate.OnThrottled(null, now, now);
            delays.Add(result.Delay.TotalSeconds);
            now = now.Add(result.Delay); // the next request is made after the pause ended
        }

        Assert.Equal([1d, 2d, 4d, 5d, 5d], delays);
    }

    [Fact]
    public void Repeated_Rate_Limits_Escalate_Even_When_The_Node_Asks_For_A_Tiny_Retry_After()
    {
        var gate = Gate(maxDelaySeconds: 60);
        var now = Start;
        var delays = new List<double>();
        for (var i = 0; i < 4; i++)
        {
            var result = gate.OnThrottled(TimeSpan.Zero, now, now);
            delays.Add(result.Delay.TotalSeconds);
            now = now.Add(result.Delay);
        }

        // The first pause honours the node (the 5 ms floor); a node that keeps answering 429 is then given more and more time.
        Assert.Equal([0.005, 2d, 4d, 8d], delays);
    }

    [Fact]
    public void Responses_Of_Requests_Made_Before_The_Pause_Do_Not_Punish_The_Host_Again()
    {
        var gate = Gate();
        gate.OnThrottled(null, Start, Start);

        // Both requests entered before the pause ended; one answers during the pause, the other after it.
        var during = gate.OnThrottled(null, Start, Start.AddMilliseconds(50));
        var after = gate.OnThrottled(null, Start, Start.AddSeconds(2));

        Assert.False(during.NewPause);
        Assert.False(after.NewPause);
        Assert.Equal(16, gate.Limit);
        Assert.Equal(Start.AddSeconds(1), gate.BlockedUntil);
    }

    [Fact]
    public void A_Longer_Retry_After_From_A_Late_Response_Extends_The_Pause_Without_Halving_Again()
    {
        var gate = Gate();
        gate.OnThrottled(null, Start, Start);

        gate.OnThrottled(TimeSpan.FromSeconds(10), Start, Start.AddMilliseconds(50));

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
            now = now.Add(gate.OnThrottled(TimeSpan.FromSeconds(1), now, now).Delay);
        }

        Assert.Equal(1, gate.Limit);
    }

    [Fact]
    public void Successes_Grow_The_Limit_Back_By_One_Per_Limit_Worth_Of_Requests_Up_To_The_Maximum()
    {
        var gate = Gate(max: 8);
        var now = Start;
        now = now.Add(gate.OnThrottled(TimeSpan.FromSeconds(1), now, now).Delay); // limit 4

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
        now = now.Add(gate.OnThrottled(null, now, now).Delay);
        now = now.Add(gate.OnThrottled(null, now, now).Delay);

        gate.OnSuccess(now);

        Assert.Equal(TimeSpan.FromSeconds(1), gate.OnThrottled(null, now, now).Delay);
    }

    [Fact]
    public void A_Success_Of_A_Request_Made_Before_The_Pause_Says_Nothing_About_The_Node_Now()
    {
        var gate = Gate(max: 8);
        var entered = Start;
        var now = Start.AddMilliseconds(10);
        gate.OnThrottled(null, entered, now); // limit 4, paused until now + 1 s
        var after = now.AddSeconds(1);
        gate.OnThrottled(null, after, after); // second pause in a row (2 s), limit 2

        gate.OnSuccess(entered); // a slow request from before both pauses finally delivers

        Assert.Equal(2, gate.Limit);
        Assert.Equal(TimeSpan.FromSeconds(4), gate.OnThrottled(null, after.AddSeconds(2), after.AddSeconds(2)).Delay);
    }

    [Fact]
    public void A_Late_Rate_Limit_After_A_Fresh_Success_Keeps_Sane_Numbers()
    {
        var gate = Gate(max: 8);
        gate.OnThrottled(null, Start, Start); // paused until Start + 1 s, limit 4
        gate.OnSuccess(Start.AddSeconds(1)); // a request admitted after the pause delivers: no pause in a row any more

        // A request admitted before the pause is answered with a 429 only now.
        var late = gate.OnThrottled(TimeSpan.FromSeconds(3), Start, Start.AddSeconds(2));

        Assert.False(late.NewPause);
        Assert.True(late.Delay >= TimeSpan.Zero);
        Assert.Equal(Start.AddSeconds(5), gate.BlockedUntil);
        Assert.True(gate.Limit >= 4);
    }

    [Fact]
    public void The_Limit_Grows_Slowly_Near_The_Level_That_Was_Too_Much()
    {
        var gate = Gate(max: 32);
        var now = Start;
        now = now.Add(gate.OnThrottled(TimeSpan.FromSeconds(1), now, now).Delay); // failed at 32, limit 16

        for (var i = 0; i < 200; i++)
        {
            gate.OnSuccess(now); // well below the failed level it grows as usual: 16 -> 24
        }

        Assert.Equal(24, gate.Limit);

        for (var i = 0; i < 100; i++)
        {
            gate.OnSuccess(now); // from three quarters of the failed level on it only probes
        }

        Assert.InRange(gate.Limit, 24, 25);

        for (var i = 0; i < 3_000; i++)
        {
            gate.OnSuccess(now); // beyond a quarter above the failed level it is quick again
        }

        Assert.Equal(32, gate.Limit);
    }

    [Fact]
    public void A_Node_That_Was_Limited_At_A_Low_Level_Is_Not_Held_Back_Above_It()
    {
        var gate = Gate(max: 32);
        var now = Start;
        now = now.Add(gate.OnThrottled(TimeSpan.FromSeconds(1), now, now).Delay); // failed at 32, limit 16
        for (var i = 0; i < 8; i++)
        {
            now = now.Add(gate.OnThrottled(TimeSpan.FromSeconds(1), now, now).Delay); // failed at 16, 8, 4, 2, 1, 1...
        }

        for (var i = 0; i < 400; i++)
        {
            gate.OnSuccess(now);
        }

        Assert.True(gate.Limit >= 20);
    }
}
