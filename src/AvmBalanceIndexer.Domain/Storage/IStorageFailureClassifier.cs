namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>
/// Tells callers that retry (the worker) which failures of the selected provider are worth waiting out: a database that is
/// starting, failing over or overloaded, as opposed to a bad query or bad credentials.
/// </summary>
public interface IStorageFailureClassifier
{
    bool IsTransient(Exception exception);
}
