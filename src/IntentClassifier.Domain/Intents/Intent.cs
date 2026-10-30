namespace IntentClassifier.Domain.Intents;

using IntentClassifier.Domain.Embeddings;

public sealed class Intent
{
    public Intent(IntentId id, string name, Embedding? nameEmbedding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
        NameEmbedding = nameEmbedding;
    }

    public IntentId Id { get; }

    public string Name { get; }

    public Embedding? NameEmbedding { get; private set; }

    public bool HasNameEmbedding => NameEmbedding is not null;

    public void AssignNameEmbedding(Embedding embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        NameEmbedding = embedding;
    }
}
