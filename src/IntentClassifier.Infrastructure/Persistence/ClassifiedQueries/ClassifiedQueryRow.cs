namespace IntentClassifier.Infrastructure.Persistence.ClassifiedQueries;

using Pgvector;

// The two classified_queries columns Confirm reads.
internal sealed class ClassifiedQueryRow
{
    public Vector Embedding { get; set; } = null!;
    public string? ConfirmedIntentId { get; set; }
}
