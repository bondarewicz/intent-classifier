namespace IntentClassifier.Api.Endpoints;

using System.ComponentModel.DataAnnotations;
using Application.Classification;

// Top is how many candidates to return when the JSON leaves it out.
public sealed record ClassifyQueryRequest(string? Query, [Range(1, int.MaxValue)] int Top = 3);

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
