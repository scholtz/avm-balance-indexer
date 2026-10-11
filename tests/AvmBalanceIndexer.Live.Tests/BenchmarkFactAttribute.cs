namespace AvmBalanceIndexer.Live.Tests;

/// <summary>A measurement that prints numbers instead of asserting them; it runs only with <c>AVM_BENCH=1</c>.</summary>
public sealed class BenchmarkFactAttribute : FactAttribute
{
    public BenchmarkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AVM_BENCH") != "1")
        {
            Skip = "Benchmark: set AVM_BENCH=1 to run it.";
        }
    }
}
