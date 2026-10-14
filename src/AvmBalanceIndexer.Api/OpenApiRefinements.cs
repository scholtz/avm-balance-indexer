using System.Text.Json.Nodes;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Tokens;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AvmBalanceIndexer.Api;

/// <summary>
/// Makes the published OpenAPI document precise enough for code generators. The handlers take their inputs as text (they parse and
/// validate them themselves, to answer 400 with a message), and the JSON options accept numbers written as strings; left alone the
/// document would type every query parameter as a string and every number as "integer or string".
/// </summary>
internal static class OpenApiRefinements
{
    private static readonly string[] Kinds = [.. Enum.GetValues<TokenKind>().Select(static k => k.ToStorageName())];

    private static readonly string[] Chains = [.. ChainId.All];

    public static Task ApplyToSchema(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        Normalize(schema);
        return Task.CompletedTask;
    }

    public static Task Apply(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "AVM Balance Indexer API";
        document.Info.Version = "v1";
        document.Info.Description = "Token balances, holders and history for Algorand-family chains (ALGO, ASA, ARC-20, ARC-200). Every deployment serves one chain under /v1/{chain}. Amounts and deltas are decimal strings; rounds are integers.";

        // Shared enums, so that generated clients get one ChainId and one TokenKind type instead of one per operation.
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas["ChainId"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "The chain a deployment indexes.",
            Enum = [.. Chains.Select(static c => (JsonNode)JsonValue.Create(c)!)],
        };
        document.Components.Schemas["TokenKind"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "algo (native, token id 0), asa (Algorand Standard Asset, also ARC-20 Smart ASAs) or arc200 (application token).",
            Enum = [.. Kinds.Select(static k => (JsonNode)JsonValue.Create(k)!)],
        };

        // Both fields of a scan request are optional (null means the default: latest round, no zero balances).
        if (document.Components.Schemas.TryGetValue("ScanRequest", out var scanRequest) && scanRequest is OpenApiSchema { Required: not null } scanRequestSchema)
        {
            scanRequestSchema.Required.Clear();
        }

        foreach (var schema in document.Components.Schemas.Values)
        {
            Normalize(schema);
        }

        foreach (var item in document.Paths.Values)
        {
            foreach (var operation in (IEnumerable<OpenApiOperation>?)item.Operations?.Values ?? [])
            {
                operation.Parameters ??= [];
                foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>())
                {
                    Refine(parameter, document);
                }

                // The route order: {chain} first, which is also the argument order of the generated methods.
                if (operation.Parameters.FirstOrDefault(static p => p.Name == "chain") is { } chainParameter && operation.Parameters.IndexOf(chainParameter) > 0)
                {
                    operation.Parameters.Remove(chainParameter);
                    operation.Parameters.Insert(0, chainParameter);
                }

                // An optional body is described as "null or ScanRequest"; generators want the plain schema and an optional body.
                if (operation.RequestBody is OpenApiRequestBody body && body.Content is not null)
                {
                    foreach (var media in body.Content.Values)
                    {
                        if (media.Schema is OpenApiSchema { OneOf: { Count: 2 } oneOf } && oneOf.FirstOrDefault(static o => o is OpenApiSchemaReference) is { } reference)
                        {
                            media.Schema = reference;
                            body.Required = false;
                        }
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    private static void Refine(OpenApiParameter parameter, OpenApiDocument document)
    {
        switch (parameter.Name)
        {
            case "chain":
                // ChainParameterTransformer declares it with the chain this deployment serves. That chain becomes the example (the Swagger UI
                // prefills it) and the type is the shared enum of all chains, so a client generated from any deployment can address any of them.
                // A default next to the reference would make the generators change the parameter order and drop the enum type.
                if (parameter.Schema is OpenApiSchema { Default: { } chainDefault })
                {
                    parameter.Example = chainDefault.DeepClone();
                }

                parameter.Schema = new OpenApiSchemaReference("ChainId", document);
                break;
            case "kind":
                parameter.Description = "Token kind: algo (native), asa (Algorand Standard Asset, also ARC-20) or arc200 (application token).";
                parameter.Schema = new OpenApiSchemaReference("TokenKind", document);
                break;
            case "tokenId":
                parameter.Description = "Asset id, application id, or 0 for the native token.";
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "uint64" };
                break;
            case "round" or "fromRound" or "toRound":
                parameter.Description = "Chain round (end-of-round state). Omit for the latest published round.";
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "uint64" };
                break;
            case "includeZero":
                parameter.Description = "Also list accounts that are opted in but hold zero.";
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Boolean };
                break;
            case "limit":
                parameter.Description = "Page size, 1 to 1000 (default 100).";
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1", Maximum = "1000" };
                break;
            case "cursor":
                parameter.Description = "Opaque cursor from the previous page's nextCursor.";
                break;
        }
    }

    /// <summary>Replaces "integer or string, matching a number" by a plain integer and drops the null placeholder enums.</summary>
    private static void Normalize(IOpenApiSchema node)
    {
        if (node is not OpenApiSchema schema)
        {
            return;
        }

        // Only a union of integer, string and null is collapsed; any other union (a reference, an object) is left as it is.
        if (schema.AnyOf is { Count: > 0 }
            && schema.AnyOf.Any(static a => a.Type is { } t && (t & JsonSchemaType.Integer) != 0)
            && schema.AnyOf.All(static a => a is OpenApiSchema { Type: { } t } && (t & ~(JsonSchemaType.Integer | JsonSchemaType.String | JsonSchemaType.Null)) == 0))
        {
            var nullable = schema.AnyOf.Any(static a => a.Type is { } t && (t & JsonSchemaType.Null) != 0) || (schema.Type is { } own && (own & JsonSchemaType.Null) != 0);
            schema.AnyOf = null;
            schema.Pattern = null;
            schema.Type = nullable ? JsonSchemaType.Integer | JsonSchemaType.Null : JsonSchemaType.Integer;
        }

        // Number handling that also reads strings makes the schema type "integer | string" (written as anyOf in 3.0): the API always writes a number.
        if (schema.Type is { } type && (type & JsonSchemaType.Integer) != 0 && (type & JsonSchemaType.String) != 0)
        {
            schema.Type = (type & JsonSchemaType.Null) != 0 ? JsonSchemaType.Integer | JsonSchemaType.Null : JsonSchemaType.Integer;
            schema.Pattern = null;
        }

        foreach (var property in schema.Properties?.Values ?? [])
        {
            Normalize(property);
        }

        if (schema.Items is not null)
        {
            Normalize(schema.Items);
        }
    }
}
