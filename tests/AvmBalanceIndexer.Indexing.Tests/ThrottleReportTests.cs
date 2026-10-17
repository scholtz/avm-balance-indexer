using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Tests;

public class ThrottleReportTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_First_Pause_Is_Reported_At_Once()
    {
        var report = new ThrottleReport(TimeSpan.FromMinutes(1));

        var summary = report.Record("http://a", 6, Start);

        Assert.NotNull(summary);
        Assert.Equal(1, summary.Pauses);
        Assert.Equal(TimeSpan.Zero, summary.Window);
    }

    [Fact]
    public void Later_Pauses_Are_Held_Back_Until_The_Interval_Has_Passed_And_Then_Summarised_Per_Node()
    {
        var report = new ThrottleReport(TimeSpan.FromMinutes(1));
        report.Record("http://a", 6, Start);

        Assert.Null(report.Record("http://a", 5, Start.AddSeconds(1)));
        Assert.Null(report.Record("http://b", 8, Start.AddSeconds(2)));
        Assert.Null(report.Record("http://a", 4, Start.AddSeconds(59)));
        var summary = report.Record("http://a", 3, Start.AddSeconds(60));

        Assert.NotNull(summary);
        Assert.Equal(TimeSpan.FromSeconds(59), summary.Window);
        Assert.Equal(4, summary.Pauses);
        Assert.Equal([new HostThrottle("http://a", 3, 3), new HostThrottle("http://b", 1, 8)], summary.Hosts);
        Assert.Equal("http://a (3 pauses, limit 3), http://b (1 pause, limit 8)", summary.Describe());
    }

    [Fact]
    public void A_Summary_Starts_The_Next_Window_Afresh()
    {
        var report = new ThrottleReport(TimeSpan.FromMinutes(1));
        report.Record("http://a", 6, Start);
        report.Record("http://a", 6, Start.AddMinutes(1));

        Assert.Null(report.Record("http://b", 2, Start.AddMinutes(1).AddSeconds(30)));
        var summary = report.Record("http://b", 2, Start.AddMinutes(2));

        Assert.Equal([new HostThrottle("http://b", 2, 2)], summary!.Hosts);
        Assert.Equal(TimeSpan.FromSeconds(30), summary.Window);
    }
}
