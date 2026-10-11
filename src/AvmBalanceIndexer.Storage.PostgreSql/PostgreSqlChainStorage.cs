using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using Npgsql;

namespace AvmBalanceIndexer.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of the storage contract for one chain. Uses PostgreSQL-specific SQL: hash-partitioned
/// account effects, range-partitioned token effects, array-parameter bulk statements, and one transaction per round
/// that writes the effects and advances the publication cursor atomically.
/// </summary>
public sealed partial class PostgreSqlChainStorage : IChainStorage
{
    private const int MaxAccountPartitions = 1024;

    private readonly NpgsqlDataSource _dataSource;
    private readonly string _chain;
    private readonly int _accountPartitions;
    private readonly HashSet<int> _knownBuckets = [];
    private readonly int _readTimeoutMilliseconds;

    public PostgreSqlChainStorage(NpgsqlDataSource dataSource, ChainContext chain, int accountHashPartitions, TimeSpan? readTimeout = null)
    {
        var timeout = readTimeout ?? TimeSpan.FromSeconds(30);
        _readTimeoutMilliseconds = timeout > TimeSpan.Zero && timeout.TotalMilliseconds <= int.MaxValue
            ? (int)timeout.TotalMilliseconds
            : throw new ArgumentOutOfRangeException(nameof(readTimeout), "The read timeout must be positive.");
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _chain = chain?.Chain ?? throw new ArgumentNullException(nameof(chain));
        _accountPartitions = accountHashPartitions is >= 1 and <= MaxAccountPartitions
            ? accountHashPartitions
            : throw new ArgumentOutOfRangeException(nameof(accountHashPartitions), $"Must be between 1 and {MaxAccountPartitions}.");
    }
}
