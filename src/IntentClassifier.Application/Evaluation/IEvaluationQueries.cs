namespace IntentClassifier.Application.Evaluation;

using Domain.Intents;
using Domain.Queries;

public sealed record EvaluationResult(
    IntentRepresentation Representation,
    DatasetSplit Split,
    long Queries,
    long Top1Correct,
    long Top3Correct,
    long Ambiguous,
    long AmbiguousTop1Correct
)
{
    public double Top1Accuracy => Queries == 0 ? 0 : (double)Top1Correct / Queries;
    public double Top3Accuracy => Queries == 0 ? 0 : (double)Top3Correct / Queries;
    public double AmbiguousTop1Accuracy =>
        Ambiguous == 0 ? 0 : (double)AmbiguousTop1Correct / Ambiguous;
    public double ConfidentTop1Accuracy =>
        Queries - Ambiguous == 0
            ? 0
            : (double)(Top1Correct - AmbiguousTop1Correct) / (Queries - Ambiguous);
}

public interface IEvaluationQueries
{
    // Classifies every embedded query of the split against the intents and compares with its label.
    Task<EvaluationResult> Evaluate(
        IntentRepresentation representation,
        DatasetSplit split,
        double ambiguousMargin,
        int top,
        CancellationToken ct = default
    );
}
