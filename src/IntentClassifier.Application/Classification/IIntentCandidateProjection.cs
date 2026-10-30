namespace IntentClassifier.Application.Classification;

using Domain.Intents;

// Rebuilds the stored top-K intent candidates for every query in the dataset.
public interface IIntentCandidateProjection
{
    Task<long> RebuildAll(
        int top,
        IntentRepresentation representation,
        CancellationToken ct = default
    );
}
