namespace IntentClassifier.Api.Endpoints;

using Application.Classification;
using Microsoft.AspNetCore.Http.HttpResults;

public static class ClassifyQueryEndpoints
{
    private const string Endpoint = "/queries/classify";

    public static void MapClassifyQueryEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost(Endpoint, Classify)
            .WithName("ClassifyQuery")
            .WithSummary(
                "Closest Banking77 intents for a raw customer query, by cosine similarity to the intent centroids; top sets how many."
            );

    // InvalidQueryException -> 400 and EmbeddingUnavailableException -> 503 are mapped by
    // ClassifyQueryExceptionHandler; only top is validated here because the handler would
    // throw ArgumentOutOfRangeException for it, which is a 500.
    private static async Task<Results<Ok<ClassifyQueryResponse>, ValidationProblem>> Classify(
        ClassifyQueryRequest request,
        ClassifyQueryHandler handler,
        CancellationToken ct
    )
    {
        if (request.Top < 1)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["top"] = ["top must be at least 1."] }
            );

        var result = await handler.Handle(request.Query ?? string.Empty, request.Top, ct: ct);
        return TypedResults.Ok(ClassifyQueryResponse.From(result));
    }
}
