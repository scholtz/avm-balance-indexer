using System.Globalization;
using System.Text;

namespace AvmBalanceIndexer.Benchmark;

/// <summary>Renders benchmark results as the markdown tables of the README and the nightly summary.</summary>
public static class Report
{
    public static string Render(IReadOnlyList<BenchmarkResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            throw new ArgumentException("At least one result file is required.", nameof(results));
        }

        var ordered = results.OrderBy(static r => r.Provider, StringComparer.Ordinal).ToArray();
        var first = ordered[0];
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Measured on {first.Date} at commit `{Short(first.Commit)}`: the {first.Rounds:N0} newest rounds of {first.Chain} (chain rounds {first.FirstChainRound:N0} to {first.LastChainRound:N0}) were indexed from an empty database by the production indexer. {(ordered.Select(static r => (r.FirstChainRound, r.LastChainRound)).Distinct().Count() == 1 ? "Both databases indexed exactly this range" : "**The ranges differ between the databases**")}, each running alone next to the API on {first.Machine}.");
        text.AppendLine();

        text.AppendLine("### Indexing and storage");
        text.AppendLine();
        text.AppendLine($"| | {string.Join(" | ", ordered.Select(static r => r.ProviderVersion))} |");
        text.AppendLine($"|---|{string.Concat(ordered.Select(static _ => "---:|"))}");
        Row(text, "Indexing speed", ordered, static r => $"{r.Indexing.RoundsPerSecond:N0} rounds/s");
        Row(text, "Effects written", ordered, static r => $"{r.Indexing.Effects:N0} ({r.Indexing.EffectsPerSecond:N0}/s)");
        Row(text, "Time for the range", ordered, static r => $"{r.Indexing.Seconds:N1} s");
        Row(text, "Stored size", ordered, static r => $"{r.Storage.TotalBytes / 1048576.0:N1} MiB{(r.Storage.IncludesUnflushedData ? " (sstables only)" : string.Empty)}");
        Row(text, "Per round", ordered, static r => $"{r.Storage.KilobytesPerRound:N2} KiB");
        Row(text, "Per effect", ordered, static r => $"{r.Storage.BytesPerEffect:N0} bytes");
        if (first.Chain == "algorand-mainnet")
        {
            Row(text, "If all 66 M MainNet rounds were like these", ordered, static r => $"{r.Storage.KilobytesPerRound * 66_000_000 / 1024.0 / 1024 / 1024:N1} TiB");
        }
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Block download is the same for both and not part of the table: {first.Download.RoundsPerSecond:N0} rounds/s from the public archival nodes ({first.Download.Transactions:N0} transactions).");
        text.AppendLine();

        text.AppendLine("### Largest tables");
        text.AppendLine();
        foreach (var result in ordered)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {result.ProviderVersion}: {string.Join(", ", result.Storage.Tables.Take(5).Select(static t => $"{t.Name} {t.Bytes / 1048576.0:N1} MiB"))}");
        }

        text.AppendLine();
        AppendAssets(text, ordered.FirstOrDefault(static r => r.Assets is not null) ?? ordered[0]);
        text.AppendLine("### API latency");
        text.AppendLine();
        text.AppendLine("One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.");
        text.AppendLine();
        var names = ordered.SelectMany(static r => r.Api).Select(static a => a.Name).Distinct().ToArray();
        text.AppendLine($"| Request | {string.Join(" | ", ordered.Select(static r => r.Provider))} |");
        text.AppendLine($"|---|{string.Concat(ordered.Select(static _ => "---:|"))}");
        foreach (var name in names)
        {
            var cells = ordered.Select(r => r.Api.FirstOrDefault(a => a.Name == name) is { } a
                ? $"{a.P50Ms:N1} / {a.P95Ms:N1} / {a.P99Ms:N1} ms; {a.RequestsPerSecond:N0} req/s, p95 {a.ConcurrentP95Ms:N0} ms{(a.Errors > 0 ? $", **{a.Errors} errors**" : string.Empty)}"
                : "n/a");
            text.AppendLine($"| {name} | {string.Join(" | ", cells)} |");
        }

        return text.ToString();
    }

    private static void AppendAssets(StringBuilder text, BenchmarkResult result)
    {
        if (result.Assets is not { } assets)
        {
            return;
        }

        text.AppendLine("### What the range contains");
        text.AppendLine();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Tokens with at least one effect in the range: {string.Join(", ", assets.TokensByKind.OrderBy(static p => p.Key, StringComparer.Ordinal).Select(static p => $"{p.Value:N0} {Label(p.Key)}"))}. {assets.AssetsCreated:N0} of those ASAs were created inside the range, {assets.Arc20Assets:N0} of them ARC-20 Smart ASAs."));
        text.AppendLine();
        text.AppendLine("Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.");
        text.AppendLine();
        text.AppendLine("| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |");
        text.AppendLine("|---|---|---:|---:|---:|");
        foreach (var token in assets.Top)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {token.Token} | {Cell(token.Name)} | {token.Effects:N0} | {token.Holders:N0} | {token.OptedIn:N0} |"));
        }

        text.AppendLine();
    }

    /// <summary>Token names are chain data written by anyone: keep them from breaking the table or injecting markup.</summary>
    private static string Cell(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : string.Concat(text.Select(static c => c switch
        {
            '|' or '[' or ']' or '<' or '>' or '`' or '*' or '_' or '&' or '!' or '#' or '~' or '\\' => "\\" + c,
            '\r' or '\n' => " ",
            _ => c.ToString(),
        }));

    private static string Label(string kind) => kind switch
    {
        "algo" => "native",
        "asa" => "ASAs",
        "arc200" => "ARC-200 tokens",
        _ => kind,
    };

    private static void Row(StringBuilder text, string label, BenchmarkResult[] results, Func<BenchmarkResult, string> value) =>
        text.AppendLine($"| {label} | {string.Join(" | ", results.Select(value))} |");

    private static string Short(string commit) => commit.Length > 8 ? commit[..8] : commit;
}
