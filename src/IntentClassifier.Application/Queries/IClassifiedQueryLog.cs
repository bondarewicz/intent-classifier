namespace IntentClassifier.Application.Queries;

using Domain.Embeddings;
using Domain.Intents;
using Domain.Queries;

// Cache and audit trail of classify calls, keyed by normalized text. The embedding is written
// once; Record refreshes the rest on every repeat.
public interface IClassifiedQueryLog
{
    Task<Embedding?> FindEmbedding(QueryText text, CancellationToken ct = default);

    Task Record(
        QueryText text,
        Embedding embedding,
        IntentRepresentation representation,
        IntentId? bestIntentId,
        double? bestSimilarity,
        bool ambiguous,
        CancellationToken ct = default
    );

    // Promotes the logged query into labelled_queries(train) atomically. Throws
    // InvalidQueryException if it was never classified, the intent is unknown, the text is in
    // the test split, or the dataset already labels it.
    Task Confirm(QueryText text, IntentId intentId, CancellationToken ct = default);
}
