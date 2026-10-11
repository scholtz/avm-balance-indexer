using System.Globalization;
using System.Text.Json;
using AvmBalanceIndexer.Benchmark;

// benchmark run    --provider postgres|cassandra [--rounds 10000] [--chain algorand-mainnet] [--api-dll path]
//                  [--postgres connection] [--cassandra host:port] [--dc dc1] [--cassandra-container name] [--out dir] [--commit sha]
// benchmark report <result.json>... [--out file.md]
var arguments = args.ToList();
if (arguments.Count == 0 || arguments[0] is not ("run" or "report"))
{
    Console.WriteLine("usage: benchmark run --provider postgres|cassandra [options] | benchmark report <result.json>... [--out file.md]");
    return 2;
}

var command = arguments[0];
string? Option(string name, string? fallback = null)
{
    var index = arguments.IndexOf("--" + name);
    return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : fallback;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

if (command == "report")
{
    var files = arguments.Skip(1).TakeWhile(static a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
    var results = files.Select(static f => JsonSerializer.Deserialize<BenchmarkResult>(File.ReadAllText(f), BenchmarkResult.Json)!).ToArray();
    var markdown = Report.Render(results);
    if (Option("out") is { } target)
    {
        File.WriteAllText(target, markdown);
    }

    Console.WriteLine(markdown);
    return 0;
}

var provider = Option("provider") ?? throw new ArgumentException("--provider is required.");
var options = new RunOptions(
    provider,
    Option("chain", "algorand-mainnet")!,
    int.Parse(Option("rounds", "10000")!, CultureInfo.InvariantCulture),
    ulong.Parse(Option("margin", "200")!, CultureInfo.InvariantCulture),
    Option("postgres", Environment.GetEnvironmentVariable("AVM_BENCH_POSTGRES")),
    Option("cassandra", Environment.GetEnvironmentVariable("AVM_BENCH_CASSANDRA")),
    Option("dc", Environment.GetEnvironmentVariable("AVM_BENCH_CASSANDRA_DC") ?? "dc1")!,
    Option("cassandra-container", Environment.GetEnvironmentVariable("AVM_BENCH_CASSANDRA_CONTAINER")),
    Option("api-dll"),
    Option("out", ".")!,
    Option("commit", Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local")!);

var result = await Runner.RunAsync(options, cancellation.Token);
Directory.CreateDirectory(options.OutputDirectory);
var path = Path.Combine(options.OutputDirectory, $"bench-{result.Provider}.json");
await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, BenchmarkResult.Json));
Console.WriteLine($"Wrote {path}");
return 0;
