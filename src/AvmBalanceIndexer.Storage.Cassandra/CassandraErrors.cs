using Cassandra;

namespace AvmBalanceIndexer.Storage.Cassandra;

/// <summary>Classifies driver failures for callers that retry (the worker).</summary>
public sealed class CassandraErrors : AvmBalanceIndexer.Domain.Storage.IStorageFailureClassifier
{
    /// <summary>
    /// Cluster trouble that a later attempt can overcome: no reachable node, timeouts, unavailable replicas, an overloaded or
    /// bootstrapping node. Query, authentication and syntax errors are not transient.
    /// </summary>
    bool AvmBalanceIndexer.Domain.Storage.IStorageFailureClassifier.IsTransient(Exception exception) => IsTransientError(exception);

    public static bool IsTransientError(Exception exception) =>
        exception is NoHostAvailableException or OperationTimedOutException or ReadTimeoutException or WriteTimeoutException
            or UnavailableException or OverloadedException or IsBootstrappingException or ReadFailureException or WriteFailureException;
}
