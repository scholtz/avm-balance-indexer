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

        Assert.False(wait.Next(5).GiveUp);
        time.Advance(TimeSpan.FromHours(25));
        Assert.False(wait.Next(5).GiveUp);
        time.Advance(TimeSpan.FromHours(1));
        Assert.True(wait.Next(5).GiveUp);

        // Progress means the quota was renewed in between: the time starts again.
        Assert.False(wait.Next(6).GiveUp);
        time.Advance(TimeSpan.FromHours(25));
        Assert.False(wait.Next(6).GiveUp);
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
