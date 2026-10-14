namespace AvmBalanceIndexer.Api;

/// <summary>Documentation metadata shared by the endpoints (the handlers return <see cref="IResult"/>, so response types are declared here).</summary>
internal static class ApiMetadata
{
    /// <summary>Declares the error responses an endpoint can produce.</summary>
    public static RouteHandlerBuilder WithErrors(this RouteHandlerBuilder builder, params int[] statuses)
    {
        foreach (var status in statuses)
        {
            builder.Produces<ErrorResponse>(status);
        }

        return builder;
    }

    /// <summary>The errors every data endpoint can return: rate limiting, a store that has nothing yet, and a request that took too long.</summary>
    public static RouteHandlerBuilder WithCommonErrors(this RouteHandlerBuilder builder)
    {
        return builder.WithErrors(StatusCodes.Status429TooManyRequests, StatusCodes.Status503ServiceUnavailable, StatusCodes.Status504GatewayTimeout);
    }
}
