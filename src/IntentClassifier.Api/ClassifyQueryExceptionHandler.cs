namespace IntentClassifier.Api;

using Application.Abstractions;
using Domain.Queries;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

// Maps exceptions a query can fail with to the response they document. Anything else falls
// through to the default ProblemDetails response ASP.NET Core writes for an unhandled 500.
internal sealed class ClassifyQueryExceptionHandler(IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken ct
    )
    {
        var problem = exception switch
        {
            InvalidQueryException e => new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["query"] = [e.Message] }
            )
            {
                Status = StatusCodes.Status400BadRequest,
            },
            EmbeddingUnavailableException => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail =
                    "Query is not in the dataset and no OpenAI API key is configured to embed it.",
            },
            System.ClientModel.ClientResultException or HttpRequestException => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                // Not exception.Message: OpenAI echoes part of the API key on a 401.
                Detail = "The embedding service failed. Try again later.",
            },
            _ => null,
        };

        if (problem is null)
            return false;

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(
            new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem }
        );
    }
}
