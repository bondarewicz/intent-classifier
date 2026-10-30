namespace IntentClassifier.Domain.Classification;

public static class ClassificationConfidence
{
    // When the best intent is less than this ahead of the runner-up the classification is ambiguous:
    // the embedding alone cannot tell the two apart reliably.
    public const double AmbiguousMargin = 0.02;

    public static bool IsAmbiguous(IReadOnlyList<IntentCandidate> candidates) =>
        candidates.Count >= 2
        && candidates[0].Similarity - candidates[1].Similarity < AmbiguousMargin;
}
