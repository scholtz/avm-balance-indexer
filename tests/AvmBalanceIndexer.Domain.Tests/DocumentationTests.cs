using System.Text.RegularExpressions;

namespace AvmBalanceIndexer.Domain.Tests;

/// <summary>Keeps the operations runbook honest: every setting and metric it names has to exist in the code.</summary>
public partial class DocumentationTests
{
    private static readonly string Root = FindRoot();

    private static readonly Lazy<IReadOnlyList<string>> SourceFiles = new(() =>
        [.. Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))]);

    /// <summary>Configuration section to the class that binds it.</summary>
    private static readonly Dictionary<string, string> Sections = new(StringComparer.Ordinal)
    {
        ["Storage"] = "StorageOptions",
        ["Storage:PostgreSql"] = "PostgreSqlOptions",
        ["Storage:Cassandra"] = "CassandraOptions",
        ["Algorand"] = "AlgorandChainOptions",
        ["Indexer"] = "IndexerOptions",
        ["Worker"] = "WorkerOptions",
        ["Scans"] = "ScanOptions",
        ["Api"] = "ApiOptions",
    };

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AvmBalanceIndexer.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    [GeneratedRegex(@"`((?:Storage|Algorand|Indexer|Worker|Scans|Api|Telemetry):[A-Za-z0-9:]+)`")]
    private static partial Regex ConfigKey();

    [GeneratedRegex(@"`(avm\.[a-z_.]+)`")]
    private static partial Regex Metric();

    /// <summary>The text of one options class, found by its name among the source files.</summary>
    private static string ClassText(string name)
    {
        foreach (var file in SourceFiles.Value)
        {
            var text = File.ReadAllText(file);
            var start = Regex.Match(text, $@"public (?:sealed )?(?:partial )?class {name}\b", RegexOptions.None, TimeSpan.FromSeconds(5));
            if (!start.Success)
            {
                continue;
            }

            var next = Regex.Match(text[(start.Index + 1)..], @"\npublic (?:sealed |static )?(?:partial )?(?:class|record|enum|interface) ", RegexOptions.None, TimeSpan.FromSeconds(5));
            return next.Success ? text.Substring(start.Index, next.Index + 1) : text[start.Index..];
        }

        throw new InvalidOperationException($"Options class {name} was not found.");
    }

    [Fact]
    public void Every_Setting_The_Runbook_Names_Exists_On_The_Class_That_Binds_Its_Section()
    {
        var runbook = File.ReadAllText(Path.Combine(Root, "docs", "operations.md"));

        var keys = ConfigKey().Matches(runbook).Select(static m => m.Groups[1].Value).Distinct().ToArray();

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            var section = key[..key.LastIndexOf(':')];
            var property = key[(key.LastIndexOf(':') + 1)..];
            if (key == "Telemetry:Enabled")
            {
                Assert.Contains("\"Telemetry:Enabled\"", File.ReadAllText(Path.Combine(Root, "src", "AvmBalanceIndexer.Hosting", "TelemetryExtensions.cs")), StringComparison.Ordinal);
                continue;
            }

            Assert.True(Sections.TryGetValue(section, out var type), $"{key}: the section {section} is not a known options section.");
            Assert.True(
                Regex.IsMatch(ClassText(type!), $@"public [\w<>\[\]?, ]+ {property} \{{ get; set; \}}", RegexOptions.None, TimeSpan.FromSeconds(5)),
                $"{key} is in the runbook but {type} has no property {property}.");
        }
    }

    [Fact]
    public void Every_Metric_The_Runbook_Names_Is_Registered_On_The_Worker_Meter()
    {
        var runbook = File.ReadAllText(Path.Combine(Root, "docs", "operations.md"));
        var registrations = File.ReadAllText(Path.Combine(Root, "src", "AvmBalanceIndexer.Worker", "WorkerHealth.cs"));

        var metrics = Metric().Matches(runbook).Select(static m => m.Groups[1].Value).Distinct().ToArray();

        Assert.NotEmpty(metrics);
        foreach (var metric in metrics)
        {
            Assert.Matches($@"Create(?:Observable)?(?:Gauge|Counter)(?:<\w+>)?\(\s*""{Regex.Escape(metric)}""", registrations);
        }
    }

    [Fact]
    public void The_Readme_And_The_Deploy_Guide_Point_To_The_Runbook()
    {
        Assert.Contains("docs/operations.md", File.ReadAllText(Path.Combine(Root, "README.md")), StringComparison.Ordinal);
        Assert.Contains("operations.md", File.ReadAllText(Path.Combine(Root, "deploy", "README.md")), StringComparison.Ordinal);
    }
}
