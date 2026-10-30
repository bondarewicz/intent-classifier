namespace IntentClassifier.Application.Queries;

using IntentClassifier.Application.Abstractions;
using IntentClassifier.Domain.Classification;
using IntentClassifier.Domain.Embeddings;
using IntentClassifier.Domain.Intents;
using IntentClassifier.Domain.Queries;

public enum EmbeddingSource
{
    Stored,
    Generated,
}

public sealed record ClassifyQueryResult(
    string RawQuery,
    QueryText Text,
    EmbeddingSource EmbeddingSource,
    bool IsAmbiguous,
    IReadOnlyList<IntentCandidate> Candidates
);

// Reuses a stored embedding when the text is known, else generates one; ranks intents by cosine
// similarity and records every successful classification to the log.
public sealed class ClassifyQueryHandler(
    ILabelledQueryRepository queries,
    IClassifiedQueryLog log,
    IIntentRepository intents,
    IEmbeddingGenerator embeddingGenerator
)
{
    public async Task<ClassifyQueryResult> Handle(
        string rawQuery,
        int top,
        IntentRepresentation representation = IntentRepresentation.Centroid,
        CancellationToken ct = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        var text = QueryText.Create(rawQuery);
        var known = await queries.FindByText(text, ct);

        Embedding embedding;
        EmbeddingSource source;
        if (known?.Embedding is { } stored)
        {
            embedding = stored;
            source = EmbeddingSource.Stored;
        }
        else if (await log.FindEmbedding(text, ct) is { } logged)
        {
            embedding = logged;
            source = EmbeddingSource.Stored;
        }
        else
        {
            embedding = await embeddingGenerator.Generate(text.Value, ct);
            source = EmbeddingSource.Generated;
        }

        // Ambiguity needs the runner-up even when only the best candidate is requested.
        var candidates = await intents.FindClosest(embedding, Math.Max(top, 2), representation, ct);
        var best = candidates.Count > 0 ? candidates[0] : null;
        var ambiguous = ClassificationConfidence.IsAmbiguous(candidates);

        await log.Record(
            text,
            embedding,
            representation,
            best?.IntentId,
            best?.Similarity,
            ambiguous,
            ct
        );

        return new ClassifyQueryResult(
            rawQuery,
            text,
            source,
            ambiguous,
            candidates.Take(top).ToList()
        );
    }
}
