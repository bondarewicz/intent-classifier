namespace IntentClassifier.Api.Endpoints;

using Application.Queries;

public sealed record ClassifyQueryRequest(string? Query);

public sealed record IntentCandidateResponse(
    int Rank,
    string IntentId,
    string IntentName,
    double Similarity
);

public sealed record ClassifyQueryResponse(
    string Query,
    string NormalizedQuery,
    string EmbeddingSource,
    bool Ambiguous,
    IReadOnlyList<IntentCandidateResponse> Candidates
)
{
    public static ClassifyQueryResponse From(ClassifyQueryResult result) =>
        new(
            result.RawQuery,
            result.Text.Value,
            result.EmbeddingSource switch
            {
                Application.Queries.EmbeddingSource.Stored => "stored",
                Application.Queries.EmbeddingSource.Generated => "openai",
                _ => throw new ArgumentOutOfRangeException(nameof(result)),
            },
            result.IsAmbiguous,
            result
                .Candidates.Select(m => new IntentCandidateResponse(
                    m.Rank,
                    m.IntentId.Value,
                    m.IntentName,
                    m.Similarity
                ))
                .ToList()
        );
}
