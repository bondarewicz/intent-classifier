namespace IntentClassifier.Infrastructure.Persistence.Reports;

// Projection of the per-split summary over the query_classification_report view.
internal sealed class SplitSummaryRow
{
    public string Split { get; set; } = "";
    public long Queries { get; set; }
    public double AverageBestSimilarity { get; set; }
    public long Correct { get; set; }
    public long Ambiguous { get; set; }
}

// Projection of an evaluation run.
internal sealed class EvaluationRow
{
    public long Queries { get; set; }
    public long Top1Correct { get; set; }
    public long Top3Correct { get; set; }
    public long Ambiguous { get; set; }
    public long AmbiguousTop1Correct { get; set; }
}

// Shape of the columns the report queries actually read from the query_classification_report view.
internal sealed class QueryClassificationReportRow
{
    public string Split { get; set; } = "";
    public double BestSimilarity { get; set; }
    public double Margin { get; set; }
    public bool IsCorrect { get; set; }
}
