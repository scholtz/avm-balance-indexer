using AvmBalanceIndexer.Domain.Storage;

namespace AvmBalanceIndexer.Storage.Cassandra;

public sealed partial class CassandraChainStorage
{
    public async Task AppendAsync(IndexingTerm term, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(term);

        // Conditional, so the first time of a term is kept; and only ever conditional, because mixing conditional and plain writes on a row
        // lets a skewed client clock shadow the conditional one.
        var insert = await BindAsync(
            $"INSERT INTO {Table("indexing_term")} (chain, from_round, version, started_at) VALUES (?, ?, ?, ?) IF NOT EXISTS",
            cancellationToken,
            _chain,
            unchecked((long)term.FromRound),
            term.Version,
            term.StartedAt).ConfigureAwait(false);
        await ExecuteConditionalAsync(insert, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IndexingTerm>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await ExecuteAsync($"SELECT from_round, version, started_at FROM {Table("indexing_term")} WHERE chain = ?", cancellationToken, _chain).ConfigureAwait(false);
        // The clustering order gives the highest round first; of several builds recorded at one round the latest start is the one that wrote.
        return [.. rows
            .Select(static row => new IndexingTerm(unchecked((ulong)row.GetValue<long>("from_round")), row.GetValue<string>("version"), row.GetValue<DateTimeOffset>("started_at")))
            .OrderByDescending(static t => t.FromRound)
            .ThenByDescending(static t => t.StartedAt)
            .ThenBy(static t => t.Version, StringComparer.Ordinal)];
    }
}
