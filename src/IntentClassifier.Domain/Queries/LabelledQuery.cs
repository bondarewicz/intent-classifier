namespace IntentClassifier.Domain.Queries;

using Embeddings;
using Intents;

// A customer query from the dataset with its known (human-labelled) intent.
public sealed class LabelledQuery(
    Guid id,
    QueryText text,
    DatasetSplit split,
    IntentId intentId,
    Embedding? embedding = null
)
{
    public Guid Id { get; } = id;

    public QueryText Text { get; } = text;

    public DatasetSplit Split { get; } = split;

    public IntentId IntentId { get; } = intentId;

    public Embedding? Embedding { get; private set; } = embedding;

    public bool HasEmbedding => Embedding is not null;

    public void AssignEmbedding(Embedding embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        Embedding = embedding;
    }
}
