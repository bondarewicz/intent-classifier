namespace IntentClassifier.Api.Endpoints;

using Application.Classification;

// Top is how many candidates to return; omitted means 3.
public sealed record ClassifyQueryRequest(string? Query, int? Top = null);

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
                Application.Classification.EmbeddingSource.Stored => "stored",
                Application.Classification.EmbeddingSource.Generated => "openai",
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
