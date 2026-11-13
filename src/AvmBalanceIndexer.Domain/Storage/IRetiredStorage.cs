namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>
/// Data sets this deployment used before a rebuild and no longer needs (ADR 0020). Listing one in the configuration is the
/// reviewed request to delete it; the writer carries the request out once the rebuilt data follows the chain.
/// </summary>
public interface IRetiredStorage
{
    /// <summary>Names of the data sets the configuration asks to remove; empty when there is nothing to do.</summary>
    IReadOnlyList<string> Retired { get; }

    /// <summary>
    /// Removes every retired data set that can be removed safely and says what happened to each. Idempotent: a data set that is
    /// already gone is reported as <see cref="RetirementOutcome.Absent"/>.
    /// </summary>
    Task<IReadOnlyList<RetirementResult>> RemoveRetiredAsync(CancellationToken cancellationToken);
}

public enum RetirementOutcome
{
    /// <summary>The data set existed and was deleted.</summary>
    Removed = 0,

    /// <summary>Nothing by that name exists (an earlier run removed it).</summary>
    Absent = 1,

    /// <summary>Somebody still writes to it; a later attempt may succeed.</summary>
    InUse = 2,

    /// <summary>It is not something this deployment may delete (another chain's data, or not indexer data at all); retrying does not help.</summary>
    Refused = 3,
}

/// <param name="Name">The retired data set.</param>
/// <param name="Outcome">What happened to it.</param>
/// <param name="Reason">Why it was not removed; empty otherwise.</param>
public sealed record RetirementResult(string Name, RetirementOutcome Outcome, string Reason = "");
