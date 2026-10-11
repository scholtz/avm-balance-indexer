using System.Net;
using AvmBalanceIndexer.Storage.Cassandra;
using Cassandra;

namespace AvmBalanceIndexer.Storage.ContractTests;

public class CassandraErrorsTests
{
    [Fact]
    public void Cluster_Trouble_Is_Transient_And_Query_Errors_Are_Not()
    {
        Assert.True(CassandraErrors.IsTransientError(new NoHostAvailableException(new Dictionary<IPEndPoint, Exception>())));
        Assert.True(CassandraErrors.IsTransientError(new OperationTimedOutException(new IPEndPoint(IPAddress.Loopback, 9042), 1000)));
        Assert.False(CassandraErrors.IsTransientError(new InvalidQueryException("bad column")));
        Assert.False(CassandraErrors.IsTransientError(new InvalidOperationException()));
    }
}
