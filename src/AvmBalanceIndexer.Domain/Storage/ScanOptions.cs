using System.ComponentModel.DataAnnotations;

namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Bound from the Scans configuration section; used by the API (admission, retention) and by the worker (execution).</summary>
public sealed class ScanOptions : IValidatableObject
{
    public const string SectionName = "Scans";

    /// <summary>Scans that may be accepted or running at once; further requests are refused until some finish.</summary>
    [Range(1, 10_000)]
    public int MaxActive { get; set; } = 20;

    /// <summary>How long a scan and its result are kept after it was accepted.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "30.00:00:00")]
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Scans one worker replica runs at the same time, so one huge export does not hold up the others.</summary>
    [Range(1, 16)]
    public int Concurrency { get; set; } = 2;

    /// <summary>How often an idle worker replica looks for a scan to run.</summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:10:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>A running scan whose owner has not checkpointed for this long is taken over by another replica.</summary>
    [Range(typeof(TimeSpan), "00:00:10", "01:00:00")]
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Holders read and stored per step; the memory a scan needs.</summary>
    [Range(1, 1000)]
    public int PageSize { get; set; } = 1000;

    /// <summary>Retries of one step after a transient failure before the scan is failed.</summary>
    [Range(0, 100)]
    public int MaxStepRetries { get; set; } = 5;

    /// <summary>Wait between retries of one step.</summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "00:10:00")]
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // The owner checkpoints at least once per retry; if a scan could look abandoned in between, two replicas would fight over it.
        if (StaleAfter <= RetryDelay * 2)
        {
            yield return new ValidationResult("Scans:StaleAfter must be more than twice Scans:RetryDelay.", [nameof(StaleAfter)]);
        }
    }
}
