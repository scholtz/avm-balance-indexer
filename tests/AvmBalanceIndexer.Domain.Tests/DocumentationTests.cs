using System.Text.RegularExpressions;

namespace AvmBalanceIndexer.Domain.Tests;

/// <summary>Keeps the operations runbook honest: every setting and metric it names has to exist in the code.</summary>
public partial class DocumentationTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AvmBalanceIndexer.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    private static string AllSource() =>
        string.Join('\n', Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));

    [GeneratedRegex(@"`((?:Storage|Algorand|Indexer|Worker|Scans|Api|Telemetry):[A-Za-z:]+)`")]
    private static partial Regex ConfigKey();

    [GeneratedRegex(@"`(avm\.[a-z_.]+)`")]
    private static partial Regex Metric();

    [Fact]
    public void Every_Setting_The_Runbook_Names_Exists_In_The_Code()
    {
        var source = AllSource();
        var runbook = File.ReadAllText(Path.Combine(Root, "docs", "operations.md"));

        var keys = ConfigKey().Matches(runbook).Select(static m => m.Groups[1].Value).Distinct().ToArray();

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            var property = key[(key.LastIndexOf(':') + 1)..];
            Assert.True(
                Regex.IsMatch(source, $@"public [\w<>\[\]?, ]+ {property} \{{ get; set; \}}", RegexOptions.None, TimeSpan.FromSeconds(5)),
                $"{key} is in the runbook but no option property {property} exists.");
        }
    }

    [Fact]
    public void Every_Metric_The_Runbook_Names_Is_Published_By_The_Code()
    {
        var source = AllSource();
        var runbook = File.ReadAllText(Path.Combine(Root, "docs", "operations.md"));

        var metrics = Metric().Matches(runbook).Select(static m => m.Groups[1].Value).Distinct().ToArray();

        Assert.NotEmpty(metrics);
        foreach (var metric in metrics)
        {
            Assert.Contains($"\"{metric}\"", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_Readme_And_The_Deploy_Guide_Point_To_The_Runbook()
    {
        Assert.Contains("docs/operations.md", File.ReadAllText(Path.Combine(Root, "README.md")), StringComparison.Ordinal);
        Assert.Contains("operations.md", File.ReadAllText(Path.Combine(Root, "deploy", "README.md")), StringComparison.Ordinal);
    }
}
