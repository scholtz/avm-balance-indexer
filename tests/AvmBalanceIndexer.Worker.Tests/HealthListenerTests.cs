using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Worker.Tests;

public class HealthListenerTests
{
    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static async Task<HttpStatusCode> GetAsync(HttpClient client, int port, string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            try
            {
                using var response = await client.GetAsync(new Uri($"http://localhost:{port}{path}"), timeout.Token);
                return response.StatusCode;
            }
            catch (HttpRequestException)
            {
                // The listener is still starting.
                await Task.Delay(20, timeout.Token);
            }
        }
    }

    [Fact]
    public async Task Liveness_Always_Answers_And_Readiness_Waits_Until_The_Role_Is_Known()
    {
        var port = FreePort();
        using var health = new WorkerHealth();
        using var listener = new HealthListener(
            Options.Create(new WorkerOptions { HealthPort = port, HealthHost = "localhost" }),
            health,
            NullLogger<HealthListener>.Instance);
        using var client = new HttpClient();

        await listener.StartAsync(CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, port, "/healthz/live"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await GetAsync(client, port, "/healthz/ready"));

        health.BecameStandby();
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, port, "/healthz/ready"));
        health.BecameLeader();
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, port, "/healthz/ready"));
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(client, port, "/other"));

        await listener.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task An_Invalid_Prefix_Switches_The_Endpoints_Off_Without_Stopping_The_Host()
    {
        using var health = new WorkerHealth();
        using var listener = new HealthListener(
            Options.Create(new WorkerOptions { HealthPort = FreePort(), HealthHost = "not a host" }),
            health,
            NullLogger<HealthListener>.Instance);

        await listener.StartAsync(CancellationToken.None);
        await listener.StopAsync(CancellationToken.None);

        Assert.Null(listener.ExecuteTask?.Exception);
    }

    [Fact]
    public async Task A_Port_Of_Zero_Turns_The_Endpoints_Off()
    {
        using var health = new WorkerHealth();
        using var listener = new HealthListener(Options.Create(new WorkerOptions { HealthPort = 0 }), health, NullLogger<HealthListener>.Instance);

        await listener.StartAsync(CancellationToken.None);
        await listener.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Metrics_Report_The_Role_And_The_Counters()
    {
        var meterName = $"{WorkerHealth.MeterName}.Test.{Guid.NewGuid():N}";
        using var health = new WorkerHealth(meterName);
        health.ReportRoundsSource(() => 42);
        var observed = new Dictionary<string, long>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == meterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<int>((instrument, value, _, _) => observed[instrument.Name] = value);
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) => observed[instrument.Name] = value);
        meterListener.Start();

        health.BecameLeader();
        health.ReportTransientFailure();
        health.LostLease();
        meterListener.RecordObservableInstruments();

        Assert.Equal(0, observed["avm.worker.is_leader"]);
        Assert.Equal(1, observed["avm.worker.leases_acquired"]);
        Assert.Equal(1, observed["avm.worker.leases_lost"]);
        Assert.Equal(1, observed["avm.worker.transient_failures"]);
        Assert.Equal(42, observed["avm.worker.rounds_committed"]);
    }
}
