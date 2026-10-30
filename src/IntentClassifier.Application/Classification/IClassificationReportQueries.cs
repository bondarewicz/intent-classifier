namespace IntentClassifier.Application.Classification;

using IntentClassifier.Domain.Queries;

public sealed record SplitSummary(
    DatasetSplit Split,
    long Queries,
    double AverageBestSimilarity,
    long Correct,
    long Ambiguous
);

// Read side over the stored candidates (query_classification_report).
public interface IClassificationReportQueries
{
    Task<IReadOnlyList<SplitSummary>> GetSummary(
        double ambiguousMargin,
        CancellationToken ct = default
    );
}
