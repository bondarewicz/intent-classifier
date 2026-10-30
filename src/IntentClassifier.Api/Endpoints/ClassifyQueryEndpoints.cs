namespace IntentClassifier.Api.Endpoints;

using IntentClassifier.Application.Queries;
using Microsoft.AspNetCore.Http.HttpResults;

public static class ClassifyQueryEndpoints
{
    private const int TopCandidates = 3;

    public static IEndpointRouteBuilder MapClassifyQueryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/queries/classify", Classify)
            .WithName("ClassifyQuery")
            .WithSummary(
                "Top 3 Banking77 intents for a raw customer query, by cosine similarity to the intent centroids."
            );

        return app;
    }

    // InvalidQueryException -> 400 and EmbeddingUnavailableException -> 503 are mapped by
    // ClassifyQueryExceptionHandler, so this is the happy path only.
    private static async Task<Ok<ClassifyQueryResponse>> Classify(
        ClassifyQueryRequest request,
        ClassifyQueryHandler handler,
        CancellationToken ct
    )
    {
        var result = await handler.Handle(request.Query ?? string.Empty, TopCandidates, ct: ct);
        return TypedResults.Ok(ClassifyQueryResponse.From(result));
    }
}
