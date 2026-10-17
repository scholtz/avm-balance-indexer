# ADR 0018: Serilog for structured logging

- Status: accepted
- Date: 2026-10-17
- Issue: #126

## Context

The API and the worker used the default Microsoft console logger: unstructured text, levels from `Logging:LogLevel`, no per-request line in the API, and nothing a
log collector could parse. The code already logs with `LoggerMessage` templates, so named properties exist.

## Decision

- Both processes call `AddAvmLogging` (Hosting). Serilog replaces the logging providers; code keeps using `ILogger<T>`.
- **Serilog is the only place levels are decided.** The framework's filter rules (`Logging:LogLevel`) are cleared, otherwise a stale `Logging__LogLevel__*` variable would hide
  events from Serilog. Levels, overrides and sinks come from the `Serilog` section (`ReadFrom.Configuration`), also from `Serilog__...` environment variables. Defaults: `Information`,
  `Microsoft.AspNetCore` and the `HttpClient` message handler at `Warning`.
- The console sink is the default: text for people, compact JSON (`RenderedCompactJsonFormatter`, `Logging:Format=Json`) for collectors; the Helm chart defaults to JSON. A configured
  `Serilog:WriteTo` replaces the default console, so any Serilog sink package can be added without changing code.
- Events carry `Service` (`avm-api`, `avm-worker`) and `Chain`.
- The API logs one line per request with Serilog.AspNetCore: Information; what a client can cause at will (health probes, `429`, abandoned requests) at Debug; 5xx and exceptions at Error. The path is logged with control characters escaped (no forged lines in the text format). The middleware is given the host's logger (not the
  process-wide static one), so several hosts in one process (tests) do not write into each other's sinks.

## Consequences

- Operators who set `Logging__LogLevel__*` must switch to `Serilog__MinimumLevel__*` (no deployment in this repository did); a startup warning names the ignored setting.
- The console sink is synchronous; stdout of a container is not expected to block. Add an async wrapper through `Serilog:WriteTo` if that changes.
- Metrics stay with OpenTelemetry (ADR 0011); logs are not exported over OTLP, add a Serilog OpenTelemetry sink through `Serilog:WriteTo` if needed.
