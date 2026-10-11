using System.Net;
using AvmBalanceIndexer.Storage.Cassandra;
using Cassandra;

namespace AvmBalanceIndexer.Storage.ContractTests;

public class CassandraErrorsTests
{
    [Fact]
    public void Cluster_Trouble_Is_Transient_And_Query_Errors_Are_Not()
    {
        Assert.True(CassandraErrors.IsTransient(new NoHostAvailableException(new Dictionary<IPEndPoint, Exception>())));
        Assert.True(CassandraErrors.IsTransient(new OperationTimedOutException(new IPEndPoint(IPAddress.Loopback, 9042), 1000)));
        Assert.False(CassandraErrors.IsTransient(new InvalidQueryException("bad column")));
        Assert.False(CassandraErrors.IsTransient(new InvalidOperationException()));
    }
}
