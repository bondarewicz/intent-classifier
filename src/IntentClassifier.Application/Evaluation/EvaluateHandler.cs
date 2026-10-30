namespace IntentClassifier.Application.Evaluation;

using Domain.Classification;
using Domain.Intents;
using Domain.Queries;

// Accuracy of each intent representation on the held-out test split.
public sealed class EvaluateHandler(IEvaluationQueries evaluation)
{
    // How many ranked candidates count toward Top3Correct/Top3Accuracy.
    private const int TopN = 3;

    public async Task<IReadOnlyList<EvaluationResult>> Handle(CancellationToken ct = default)
    {
        var results = new List<EvaluationResult>();
        foreach (var representation in Enum.GetValues<IntentRepresentation>())
            results.Add(
                await evaluation.Evaluate(
                    representation,
                    DatasetSplit.Test,
                    ClassificationConfidence.AmbiguousMargin,
                    TopN,
                    ct
                )
            );
        return results;
    }
}
