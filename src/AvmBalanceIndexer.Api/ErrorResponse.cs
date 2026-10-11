namespace AvmBalanceIndexer.Api;

/// <summary>Indexed round, target round, lag and ingestion state of the chain this deployment serves.</summary>
public sealed record StatusResponse(string Chain, string Provider, string State, ulong? IndexedRound, ulong? TargetRound, ulong? Lag, DateTimeOffset? UpdatedAt);

/// <summary>The body of an error response: a message, and for some errors extra fields that name the offending values.</summary>
public sealed record ErrorResponse(string Error);
