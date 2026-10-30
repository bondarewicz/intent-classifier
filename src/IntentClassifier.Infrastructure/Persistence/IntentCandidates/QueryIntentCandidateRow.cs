namespace IntentClassifier.Infrastructure.Persistence.IntentCandidates;

// Persistence model of the query_intent_candidates table.
internal sealed class QueryIntentCandidateRow
{
    public Guid QueryId { get; set; }
    public int Rank { get; set; }
    public string IntentId { get; set; } = "";
    public double Similarity { get; set; }
}
