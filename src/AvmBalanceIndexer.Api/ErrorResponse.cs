namespace AvmBalanceIndexer.Api;

/// <summary>Indexed round, target round, lag and ingestion state of the chain this deployment serves.</summary>
/// <param name="Chain">The chain this deployment serves.</param>
/// <param name="Provider">The storage provider (<c>PostgreSql</c> or <c>Cassandra</c>).</param>
/// <param name="State">What the writer published (for example <c>Backfilling</c> or <c>Following</c>), or <c>NotStarted</c> before the first round.</param>
/// <param name="IndexedRound">The newest round the API serves; null before the first round.</param>
/// <param name="TargetRound">The newest round of the chain as last seen by the writer.</param>
/// <param name="Lag">Rounds between the target round and the indexed round.</param>
/// <param name="UpdatedAt">When the writer last updated the status.</param>
/// <param name="Health">The API's view of ingestion: <c>NotStarted</c>, <c>Healthy</c>, or <c>Stalled</c> when the writer has not updated the status for longer than <c>Api:StalledAfterSeconds</c>.</param>
/// <param name="StatusAgeSeconds">Whole seconds since the writer last updated the status; never negative, null before the first round.</param>
/// <param name="HistoricalQueries">How holders at a round are computed and the limits that apply.</param>
public sealed record StatusResponse(
    string Chain,
    string Provider,
    string State,
    ulong? IndexedRound,
    ulong? TargetRound,
    ulong? Lag,
    DateTimeOffset? UpdatedAt,
    string Health,
    long? StatusAgeSeconds,
    HistoricalQueriesInfo HistoricalQueries);

/// <summary>How holders at a round are computed, and the limits a synchronous request runs into.</summary>
/// <param name="Mode">Always <c>EffectAggregation</c>: holders at a round are computed by aggregating token effects, there are no snapshots.</param>
/// <param name="SynchronousTimeoutSeconds">The smaller of the request timeout (answered with 504) and the storage read timeout (answered with 503). On Cassandra the read timeout bounds a whole read; on PostgreSQL it bounds each statement, so a request can run until the request timeout. A query that needs longer belongs in an asynchronous scan.</param>
/// <param name="AsynchronousScans">Whether holder scans can be started (<c>POST .../holders/scans</c>) for queries too large to answer synchronously.</param>
/// <param name="MaxActiveScans">How many scans may be pending or running at once.</param>
public sealed record HistoricalQueriesInfo(string Mode, int SynchronousTimeoutSeconds, bool AsynchronousScans, int MaxActiveScans);

/// <summary>The body of an error response: a message, and for some errors extra fields that name the offending values.</summary>
public sealed record ErrorResponse(string Error);
