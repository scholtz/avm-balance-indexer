namespace AvmBalanceIndexer.Worker.Tests;

public class QuotaWaitTests
{
    private static readonly TimeSpan First = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(1);

    [Fact]
    public void The_Wait_Starts_Short_And_Doubles_Up_To_The_Longest_While_Nothing_Is_Indexed()
    {
        var wait = new QuotaWait(First, Longest, new ManualTime());

        var waits = Enumerable.Range(0, 7).Select(_ => wait.Next(progress: 10).Wait.TotalSeconds).ToArray();

        Assert.Equal([2, 4, 8, 16, 32, 60, 60], waits);
    }

    [Fact]
    public void Progress_Between_Two_Refusals_Starts_The_Wait_Short_Again()
    {
        var wait = new QuotaWait(First, Longest, new ManualTime());
        wait.Next(10);
        wait.Next(10);

        Assert.Equal(First, wait.Next(11).Wait);
        Assert.Equal(First * 2, wait.Next(11).Wait);
    }

    [Fact]
    public void A_First_Wait_Above_The_Longest_Is_The_Longest()
    {
        var wait = new QuotaWait(TimeSpan.FromMinutes(5), Longest, new ManualTime());

        Assert.Equal(Longest, wait.Next(0).Wait);
    }

    [Fact]
    public void The_Answer_Is_Logged_First_And_Then_At_Most_Every_Ten_Minutes()
    {
        var time = new ManualTime();
        var wait = new QuotaWait(First, Longest, time);

        Assert.True(wait.Next(0).Log);
        time.Advance(TimeSpan.FromMinutes(9));
        Assert.False(wait.Next(0).Log);
        Assert.False(wait.Next(1).Log); // progress does not bring the line back earlier
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(wait.Next(1).Log);
        Assert.False(wait.Next(1).Log);
    }

    [Fact]
    public void A_Node_That_Refuses_Longer_Than_A_Daily_Quota_Lasts_Without_Any_Progress_Is_Given_Up_On()
    {
        var time = new ManualTime();
        var wait = new QuotaWait(First, Longest, time);

        // Refused every minute: given up on at the first refusal 26 hours after the first one.
        for (var minute = 0; minute < 26 * 60; minute++)
        {
            Assert.False(wait.Next(5).GiveUp);
            time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.True(wait.Next(5).GiveUp);

        // Progress means the quota was renewed in between: the time starts again.
        Assert.False(wait.Next(6).GiveUp);
    }

    [Fact]
    public void A_Long_Configured_Wait_Is_Not_Taken_For_A_New_Outage()
    {
        var time = new ManualTime();
        var longest = TimeSpan.FromMinutes(15); // longer than the ten minutes between two log lines
        var wait = new QuotaWait(TimeSpan.FromMinutes(5), longest, time);

        // The worker waits as long as it is told between two refusals.
        var waits = new List<double>();
        var gaveUp = false;
        for (var i = 0; i < 120 && !gaveUp; i++)
        {
            var step = wait.Next(5);
            waits.Add(step.Wait.TotalMinutes);
            gaveUp = step.GiveUp;
            time.Advance(step.Wait);
        }

        Assert.Equal(new double[] { 5, 10, 15, 15 }, waits.Take(4));
        Assert.All(waits.Skip(2), static w => Assert.Equal(15, w));
        Assert.True(gaveUp);
    }

    [Fact]
    public void Two_Refusals_Far_Apart_Are_Two_Outages_Also_Without_Progress()
    {
        var time = new ManualTime();
        var wait = new QuotaWait(First, Longest, time);
        wait.Next(5);
        wait.Next(5);

        // A chain that stands still: the node answered in between, nothing was indexed.
        time.Advance(TimeSpan.FromHours(30));
        var step = wait.Next(5);

        Assert.False(step.GiveUp);
        Assert.Equal(First, step.Wait);
    }
}

/// <summary>A clock that only moves when a test moves it.</summary>
internal sealed class ManualTime : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
