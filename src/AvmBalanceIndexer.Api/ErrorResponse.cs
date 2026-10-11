namespace AvmBalanceIndexer.Api;

/// <summary>The body of an error response: a message, and for some errors extra fields that name the offending values.</summary>
public sealed record ErrorResponse(string Error);
