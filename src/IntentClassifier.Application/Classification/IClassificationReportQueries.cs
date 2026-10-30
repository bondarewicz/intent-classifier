namespace IntentClassifier.Application.Classification;

using Domain.Intents;
using Domain.Queries;

public sealed record SplitSummary(
    DatasetSplit Split,
    long Queries,
    double AverageBestSimilarity,
    long Correct,
    long Ambiguous
);

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

// Read side over the classification results: the stored candidates (query_classification_report)
// and an on-the-fly evaluation of one split.
public interface IClassificationReportQueries
{
    Task<IReadOnlyList<SplitSummary>> GetSummary(
        double ambiguousMargin,
        CancellationToken ct = default
    );

    // Classifies every embedded query of the split against the intents and compares with its label.
    Task<EvaluationResult> Evaluate(
        IntentRepresentation representation,
        DatasetSplit split,
        double ambiguousMargin,
        int top,
        CancellationToken ct = default
    );
}
