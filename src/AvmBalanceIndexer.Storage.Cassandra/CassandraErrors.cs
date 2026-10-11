using Cassandra;

namespace AvmBalanceIndexer.Storage.Cassandra;

/// <summary>Classifies driver failures for callers that retry (the worker).</summary>
public static class CassandraErrors
{
    /// <summary>
    /// Cluster trouble that a later attempt can overcome: no reachable node, timeouts, unavailable replicas, an overloaded or
    /// bootstrapping node. Query, authentication and syntax errors are not transient.
    /// </summary>
    public static bool IsTransient(Exception exception) =>
        exception is NoHostAvailableException or OperationTimedOutException or ReadTimeoutException or WriteTimeoutException
            or UnavailableException or OverloadedException or IsBootstrappingException or ReadFailureException or WriteFailureException;
}
