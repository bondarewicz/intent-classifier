namespace IntentClassifier.Infrastructure.Persistence.Reports;

using Application.Classification;
using Application.Evaluation;
using Dapper;
using Domain.Intents;
using Domain.Queries;
using Npgsql;

internal sealed class ClassificationReportQueries(NpgsqlDataSource dataSource)
    : IClassificationReportQueries,
        IEvaluationQueries
{
    public async Task<IReadOnlyList<SplitSummary>> GetSummary(
        double ambiguousMargin,
        CancellationToken ct = default
    )
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<SplitSummaryRow>(
            new CommandDefinition(
                QueryClassificationReportSchema.Sql.SelectSummaryBySplit,
                new { Margin = ambiguousMargin },
                cancellationToken: ct
            )
        );
        return rows.Select(r => new SplitSummary(
                r.Split.ToDatasetSplit(),
                r.Queries,
                r.AverageBestSimilarity,
                r.Correct,
                r.Ambiguous
            ))
            .ToList();
    }

    public async Task<EvaluationResult> Evaluate(
        IntentRepresentation representation,
        DatasetSplit split,
        double ambiguousMargin,
        int top,
        CancellationToken ct = default
    )
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleAsync<EvaluationRow>(
            new CommandDefinition(
                QueryClassificationReportSchema.Sql.Evaluate(representation),
                new
                {
                    Split = split.ToDb(),
                    Margin = ambiguousMargin,
                    Top = top,
                },
                commandTimeout: 0,
                cancellationToken: ct
            )
        );
        return new EvaluationResult(
            representation,
            split,
            row.Queries,
            row.Top1Correct,
            row.Top3Correct,
            row.Ambiguous,
            row.AmbiguousTop1Correct
        );
    }
}
