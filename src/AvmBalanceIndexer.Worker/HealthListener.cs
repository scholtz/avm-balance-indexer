using System.Net;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Worker;

/// <summary>Settings of the worker process itself (as opposed to the indexing loop).</summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>Port of the health endpoints (/healthz/live, /healthz/ready); 0 turns them off.</summary>
    public int HealthPort { get; set; } = 8081;

    /// <summary>Host in the listener prefix; <c>+</c> listens on every interface (needs rights on Windows, use localhost there).</summary>
    public string HealthHost { get; set; } = "+";
}

/// <summary>
/// Minimal HTTP listener for Kubernetes probes, because the worker is not a web application. Liveness answers whenever the
/// process does; readiness additionally needs the replica to know its role (a standby is ready: it is meant to wait).
/// </summary>
public sealed partial class HealthListener(IOptions<WorkerOptions> options, WorkerHealth health, ILogger<HealthListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = options.Value.HealthPort;
        if (port == 0)
        {
            return;
        }

        using var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add($"http://{options.Value.HealthHost}:{port}/");
            listener.Start();
        }
        catch (Exception ex) when (ex is HttpListenerException or ArgumentException)
        {
            // Probes are an aid; an occupied or forbidden port must not stop indexing.
            LogCannotListen(logger, ex, port);
            return;
        }

        using var registration = stoppingToken.Register(listener.Stop);
        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                if (stoppingToken.IsCancellationRequested || !listener.IsListening)
                {
                    return;
                }

                // A single aborted accept must not switch the probes off for the rest of the process lifetime.
                try
                {
                    await Task.Delay(100, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            try
            {
                Respond(context);
            }
            catch (Exception ex)
            {
                // Whatever goes wrong with one probe must never reach the host: the indexer matters more than its probes.
                LogProbeFailed(logger, ex);
            }
        }
    }

    private void Respond(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath;
            context.Response.StatusCode = path switch
            {
                "/healthz/live" => 200,
                "/healthz/ready" => health.Ready ? 200 : 503,
                _ => 404,
            };
            context.Response.ContentType = "text/plain";
            var body = System.Text.Encoding.UTF8.GetBytes(path is "/healthz/live" or "/healthz/ready" ? $"{health.State}" : "not found");
            context.Response.OutputStream.Write(body);
        }
        finally
        {
            context.Response.Close();
        }
    }

    [LoggerMessage(EventId = 1311, Level = LogLevel.Debug, Message = "A health probe could not be answered")]
    private static partial void LogProbeFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1310, Level = LogLevel.Warning, Message = "Health endpoints are off: cannot listen on port {Port}")]
    private static partial void LogCannotListen(ILogger logger, Exception exception, int port);
}
