using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ats.Api.OpenApi;

// Adds the feed's "Authorization: Token {key}" API-key scheme to the OpenAPI document so Scalar
// shows an auth input for testing the vacancy feed.
internal sealed class FeedSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["FeedToken"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "Authorization",
                Description = "Feed API key. Enter exactly: Token {your-feed-key}"
            }
        };

        foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations))
        {
            operation.Value.Security ??= [];
            operation.Value.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("FeedToken", document)] = []
            });
        }

        return Task.CompletedTask;
    }
}
