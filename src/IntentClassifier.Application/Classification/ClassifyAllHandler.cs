namespace IntentClassifier.Application.Classification;

using IntentClassifier.Domain.Classification;
using IntentClassifier.Domain.Intents;

public sealed record ClassifyAllResult(long RowsStored, IReadOnlyList<SplitSummary> Summary);

// Recomputes and stores every query's top-K closest intents, then summarizes the run per split.
public sealed class ClassifyAllHandler(
    IIntentCandidateProjection candidates,
    IClassificationReportQueries report
)
{
    public async Task<ClassifyAllResult> Handle(
        int top,
        IntentRepresentation representation = IntentRepresentation.Centroid,
        CancellationToken ct = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 2); // the report needs the runner-up

        var rows = await candidates.RebuildAll(top, representation, ct);
        var summary = await report.GetSummary(ClassificationConfidence.AmbiguousMargin, ct);
        return new ClassifyAllResult(rows, summary);
    }
}
