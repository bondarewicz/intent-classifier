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

    // The request's DataAnnotations run before this (AddValidation), InvalidQueryException -> 400
    // and EmbeddingUnavailableException -> 503 are mapped by ClassifyQueryExceptionHandler, so
    // this is the happy path only.
    private static async Task<Ok<ClassifyQueryResponse>> Classify(
        ClassifyQueryRequest request,
        ClassifyQueryHandler handler,
        CancellationToken ct
    )
    {
        var result = await handler.Handle(request.Query ?? string.Empty, request.Top, ct: ct);
        return TypedResults.Ok(ClassifyQueryResponse.From(result));
    }
}
