using System.Text.Json.Nodes;
using AvmBalanceIndexer.Domain.Chains;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AvmBalanceIndexer.Api;

/// <summary>
/// The handlers read the chain from the deployment, so the framework does not describe the <c>{chain}</c> segment of the path at all and
/// "Try it out" could not fill it. This declares it, with the chain the deployment serves as its default. A default and not an enum: a client
/// generated from the description of any deployment must still be able to address another.
/// </summary>
internal sealed class ChainParameterTransformer(ChainContext chain) : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.RelativePath?.Contains("{chain}", StringComparison.Ordinal) != true)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];
        if (!operation.Parameters.Any(static p => p.Name == "chain"))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "chain",
                In = ParameterLocation.Path,
                Required = true,
                Description = "The chain this deployment serves.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Default = JsonValue.Create(chain.Chain) },
            });
        }

        return Task.CompletedTask;
    }
}
