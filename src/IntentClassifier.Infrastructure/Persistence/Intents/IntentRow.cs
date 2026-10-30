namespace IntentClassifier.Infrastructure.Persistence.Intents;

// Persistence model of the intents table.
internal sealed class IntentRow
{
    public string IntentId { get; set; } = "";
    public string IntentName { get; set; } = "";
}

// Projection returned by the nearest-intent search.
internal sealed class IntentCandidateRow
{
    public int Rank { get; set; }
    public string IntentId { get; set; } = "";
    public string IntentName { get; set; } = "";
    public double Similarity { get; set; }
}
