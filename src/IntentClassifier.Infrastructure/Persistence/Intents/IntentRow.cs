namespace IntentClassifier.Infrastructure.Persistence.Intents;

using Pgvector;

// Persistence model of the intents table.
internal sealed class IntentRow
{
    public string IntentId { get; set; } = "";
    public string IntentName { get; set; } = "";
    public Vector? NameEmbedding { get; set; }
    public Vector? CentroidEmbedding { get; set; }
    public int ExampleCount { get; set; }
    public DateTime? CentroidUpdatedAt { get; set; }
}

// Projection returned by the nearest-intent search.
internal sealed class IntentCandidateRow
{
    public int Rank { get; set; }
    public string IntentId { get; set; } = "";
    public string IntentName { get; set; } = "";
    public double Similarity { get; set; }
}
