namespace IntentClassifier.UnitTests.Domain;

using IntentClassifier.Domain.Classification;
using IntentClassifier.Domain.Intents;

public sealed class ClassificationConfidenceTests
{
    private static IntentCandidate Candidate(int rank, double similarity) =>
        new(rank, IntentId.Create($"intent_{rank}"), $"Intent {rank}", similarity);

    [Fact]
    public void Ambiguous_when_runner_up_is_within_the_margin() =>
        Assert.True(ClassificationConfidence.IsAmbiguous([Candidate(1, 0.80), Candidate(2, 0.79)]));

    [Fact]
    public void Confident_when_best_is_clearly_ahead() =>
        Assert.False(
            ClassificationConfidence.IsAmbiguous([Candidate(1, 0.80), Candidate(2, 0.70)])
        );

    [Fact]
    public void A_single_candidate_is_not_ambiguous() =>
        Assert.False(ClassificationConfidence.IsAmbiguous([Candidate(1, 0.5)]));
}
