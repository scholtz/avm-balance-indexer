#pragma warning disable CA1848, CA1873 // tests log a handful of events; delegates would only add noise

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace AvmBalanceIndexer.Hosting.Tests;

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleSerial;

 // some tests replace Console.Out
[Collection("Console")] // some tests replace Console.Out
public class LoggingExtensionsTests
{
    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            lock (Events)
            {
                Events.Add(logEvent);
            }
        }
    }

    private static (ILoggerFactory Factory, CollectingSink Sink) Build(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var sink = new CollectingSink();
        var services = new ServiceCollection();
        services.AddSingleton<ILogEventSink>(sink);
        services.AddLogging();
        services.AddAvmLogging(configuration, "avm-test");
        return (services.BuildServiceProvider().GetRequiredService<ILoggerFactory>(), sink);
    }

    [Fact]
    public void Information_Is_Logged_By_Default_And_Debug_Is_Not()
    {
        var (factory, sink) = Build();
        var logger = factory.CreateLogger("Test");

        logger.LogDebug("hidden");
        logger.LogInformation("shown {Round}", 7);

        var logged = Assert.Single(sink.Events);
        Assert.Equal("shown 7", logged.RenderMessage());
        Assert.Equal("Test", ((ScalarValue)logged.Properties["SourceContext"]).Value);
    }

    [Fact]
    public void The_Serilog_Section_Decides_The_Levels_Per_Namespace()
    {
        var (factory, sink) = Build(
            ("Serilog:MinimumLevel:Default", "Debug"),
            ("Serilog:MinimumLevel:Override:Noisy.Library", "Error"));

        factory.CreateLogger("Mine.Code").LogDebug("debug");
        factory.CreateLogger("Noisy.Library.Part").LogWarning("warning");
        factory.CreateLogger("Noisy.Library.Part").LogError("error");

        Assert.Equal(["debug", "error"], sink.Events.Select(e => e.RenderMessage()));
    }

    [Fact]
    public void The_Microsoft_Logging_Filters_Do_Not_Hide_Events_From_Serilog()
    {
        var (factory, sink) = Build(("Logging:LogLevel:Default", "Error"));
        factory.CreateLogger("Test").LogInformation("still logged");

        Assert.Single(sink.Events);
    }

    [Fact]
    public void Events_Carry_The_Service_And_The_Configured_Chain()
    {
        var (factory, sink) = Build(("Chain", "algorand-testnet"));

        factory.CreateLogger("Test").LogInformation("hello");

        var logged = Assert.Single(sink.Events);
        Assert.Equal("avm-test", ((ScalarValue)logged.Properties["Service"]).Value);
        Assert.Equal("algorand-testnet", ((ScalarValue)logged.Properties["Chain"]).Value);
    }

    [Fact]
    public void An_Empty_Chain_Is_Not_Added_To_The_Events()
    {
        var (factory, sink) = Build(("Chain", ""));

        factory.CreateLogger("Test").LogInformation("hello");

        Assert.False(Assert.Single(sink.Events).Properties.ContainsKey("Chain"));
    }

    [Theory]
    [InlineData("Json")]
    [InlineData("json")]
    [InlineData("Text")]
    [InlineData("")]
    public void Every_Console_Format_Starts_And_Logs(string format)
    {
        var (factory, sink) = Build((LoggingExtensions.FormatSetting, format));

        factory.CreateLogger("Test").LogInformation("hello");

        Assert.Single(sink.Events);
    }

    [Fact]
    public async Task The_Json_Console_Writes_One_Compact_Json_Object_Per_Event()
    {
        var original = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [LoggingExtensions.FormatSetting] = "Json", ["Chain"] = "algorand-testnet" })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAvmLogging(configuration, "avm-test");
            await using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<ILoggerFactory>().CreateLogger("Test").LogInformation("indexed {Round}", 42);
        }
        finally
        {
            Console.SetOut(original);
        }

        var line = Assert.Single(captured.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var json = System.Text.Json.JsonDocument.Parse(line);
        Assert.Equal("indexed 42", json.RootElement.GetProperty("@m").GetString());
        Assert.Equal(42, json.RootElement.GetProperty("Round").GetInt32());
        Assert.Equal("avm-test", json.RootElement.GetProperty("Service").GetString());
    }

    [Fact]
    public void Configured_Sinks_Replace_The_Default_Console()
    {
        var original = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            var (factory, sink) = Build(
                ("Serilog:WriteTo:0:Name", "Console"),
                ("Serilog:WriteTo:0:Args:outputTemplate", "CUSTOM {Message}{NewLine}"));
            factory.CreateLogger("Test").LogInformation("hello");
            Assert.Single(sink.Events);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal("CUSTOM hello", captured.ToString().Trim()); // the configured console only, not the default one in addition
    }

    [Fact]
    public async Task A_Warning_Names_The_Ignored_Logging_LogLevel_Settings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Warning" }).Build();
        var sink = new CollectingSink();
        var services = new ServiceCollection();
        services.AddSingleton<ILogEventSink>(sink);
        services.AddLogging();
        services.AddAvmLogging(configuration, "avm-test");
        await using var provider = services.BuildServiceProvider();

        foreach (var hosted in provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        var warning = Assert.Single(sink.Events, e => e.Level == LogEventLevel.Warning);
        Assert.Contains("Logging:LogLevel", warning.RenderMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_Logging_LogLevel_Settings_There_Is_No_Notice()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAvmLogging(new ConfigurationBuilder().Build(), "avm-test");

        Assert.Empty(services.BuildServiceProvider().GetServices<Microsoft.Extensions.Hosting.IHostedService>());
    }
}
