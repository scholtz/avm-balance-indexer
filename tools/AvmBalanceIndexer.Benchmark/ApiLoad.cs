using System.Diagnostics;
using System.Net;

namespace AvmBalanceIndexer.Benchmark;

/// <summary>One kind of request to measure; <see cref="Path"/> may differ per call (different accounts), the name is what the report shows.</summary>
public sealed record LoadCase(string Name, string Description, Func<int, string> Path);

/// <summary>Runs the real API as its own process against the indexed database and measures request latency and throughput.</summary>
public sealed class ApiLoad : IAsyncDisposable
{
    private const int SequentialRequests = 300;
    private const int ConcurrentRequests = 2000;
    private const int Concurrency = 16;
    private static readonly TimeSpan PhaseLimit = TimeSpan.FromSeconds(15);

    private readonly Process _process;
    private readonly HttpClient _http;

    private ApiLoad(Process process, HttpClient http)
    {
        _process = process;
        _http = http;
    }

    public static async Task<ApiLoad> StartAsync(string apiDll, IReadOnlyDictionary<string, string> environment, int port, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet", $"\"{apiDll}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var (key, value) in environment)
        {
            start.Environment[key] = value;
        }

        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["Api__RateLimitPerMinute"] = "0";
        start.Environment["Telemetry__Enabled"] = "false";
        var process = Process.Start(start) ?? throw new InvalidOperationException("The API process did not start.");
        process.OutputDataReceived += static (_, _) => { };
        process.BeginOutputReadLine();
        process.ErrorDataReceived += static (_, _) => { };
        process.BeginErrorReadLine();

        var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 64 }) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(60) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var ready = await http.GetAsync(new Uri("/healthz/ready", UriKind.Relative), cancellationToken).ConfigureAwait(false);
                if (ready.IsSuccessStatusCode)
                {
                    return new ApiLoad(process, http);
                }
            }
            catch (HttpRequestException)
            {
            }

            if (process.HasExited)
            {
                throw new InvalidOperationException($"The API process exited with code {process.ExitCode}.");
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        process.Kill(entireProcessTree: true);
        throw new TimeoutException("The API did not become ready within 90 seconds.");
    }

    public async Task<EndpointResult> MeasureAsync(LoadCase test, CancellationToken cancellationToken)
    {
        // Warm up connections, the JIT and the database caches the way a running service has them.
        for (var i = 0; i < 20; i++)
        {
            await SendAsync(test.Path(i), cancellationToken).ConfigureAwait(false);
        }

        var latencies = new List<double>(SequentialRequests);
        var errors = 0;
        var phase = Stopwatch.StartNew();
        for (var i = 0; i < SequentialRequests && phase.Elapsed < PhaseLimit; i++)
        {
            var (ok, ms) = await SendAsync(test.Path(i), cancellationToken).ConfigureAwait(false);
            latencies.Add(ms);
            errors += ok ? 0 : 1;
        }

        var concurrent = new System.Collections.Concurrent.ConcurrentBag<double>();
        var concurrentErrors = 0;
        var next = 0;
        var clock = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(async _ =>
        {
            while (clock.Elapsed < PhaseLimit)
            {
                var index = Interlocked.Increment(ref next) - 1;
                if (index >= ConcurrentRequests)
                {
                    return;
                }

                var (ok, ms) = await SendAsync(test.Path(index), cancellationToken).ConfigureAwait(false);
                concurrent.Add(ms);
                if (!ok)
                {
                    Interlocked.Increment(ref concurrentErrors);
                }
            }
        })).ConfigureAwait(false);
        clock.Stop();

        var sorted = latencies.Order().ToArray();
        var sortedConcurrent = concurrent.Order().ToArray();
        return new EndpointResult(
            test.Name,
            test.Description,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            Concurrency,
            sortedConcurrent.Length / clock.Elapsed.TotalSeconds,
            Percentile(sortedConcurrent, 0.95),
            sorted.Length + sortedConcurrent.Length,
            errors + concurrentErrors);
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }

        _process.Dispose();
    }

    private async Task<(bool Ok, double Milliseconds)> SendAsync(string path, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await _http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
            _ = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return (response.StatusCode == HttpStatusCode.OK, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private static double Percentile(double[] sorted, double fraction) =>
        sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * fraction) - 1 < 0 ? 0 : (int)Math.Ceiling(sorted.Length * fraction) - 1)];
}
