using System.Data.Common;
using AvmBalanceIndexer.Domain.Storage;

namespace AvmBalanceIndexer.Storage.PostgreSql;

/// <summary>Database errors Npgsql itself marks as transient (connection loss, failover, shutdown, too many connections).</summary>
public sealed class PostgreSqlFailureClassifier : IStorageFailureClassifier
{
    public bool IsTransient(Exception exception) => exception is DbException { IsTransient: true };
}
