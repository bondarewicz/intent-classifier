namespace IntentClassifier.Infrastructure.Persistence;

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
    // Train before test, explicitly - not relying on the alphabetical sort of the split strings.
    public async Task<IReadOnlyList<SplitSummary>> GetSummary(
        double ambiguousMargin,
        CancellationToken ct = default
    )
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<SplitSummaryRow>(
            new CommandDefinition(
                $"""
                SELECT split,
                       count(*) AS queries,
                       coalesce(avg(best_similarity), 0)::double precision AS average_best_similarity,
                       count(*) FILTER (WHERE is_correct) AS correct,
                       count(*) FILTER (WHERE margin < @Margin) AS ambiguous
                FROM {Schema.QueryClassificationReport}
                GROUP BY split
                ORDER BY (split = 'train') DESC
                """,
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

    // Classifies every embedded query of a split against the intents on the fly (top @Top) and
    // compares with the labelled intent; nothing is stored.
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
                EvaluateSql(representation),
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

    private static string EvaluateSql(IntentRepresentation representation)
    {
        var intentEmbedding = $"i.{Schema.EmbeddingColumn(representation)}";
        var distance = $"{intentEmbedding} <=> q.embedding";
        return $"""
            WITH ranked AS (
                SELECT q.query_id, q.intent_id AS expected,
                       m.intent_id, m.rank, m.similarity
                FROM {Schema.LabelledQueries} q
                CROSS JOIN LATERAL (
                    SELECT i.intent_id,
                           1 - ({distance}) AS similarity,
                           row_number() OVER (ORDER BY {distance}) AS rank
                    FROM {Schema.Intents} i
                    WHERE {intentEmbedding} IS NOT NULL
                    ORDER BY {distance}
                    LIMIT @Top
                ) m
                WHERE q.split = @Split AND q.embedding IS NOT NULL
            ),
            per_query AS (
                SELECT query_id,
                       bool_or(rank = 1 AND intent_id = expected) AS top1,
                       bool_or(intent_id = expected) AS top3,
                       coalesce(max(similarity) FILTER (WHERE rank = 1) - max(similarity) FILTER (WHERE rank = 2) < @Margin, false) AS ambiguous
                FROM ranked
                GROUP BY query_id
            )
            SELECT count(*) AS queries,
                   count(*) FILTER (WHERE top1) AS top1_correct,
                   count(*) FILTER (WHERE top3) AS top3_correct,
                   count(*) FILTER (WHERE ambiguous) AS ambiguous,
                   count(*) FILTER (WHERE ambiguous AND top1) AS ambiguous_top1_correct
            FROM per_query
            """;
    }

    private sealed class SplitSummaryRow
    {
        public string Split { get; set; } = "";
        public long Queries { get; set; }
        public double AverageBestSimilarity { get; set; }
        public long Correct { get; set; }
        public long Ambiguous { get; set; }
    }

    private sealed class EvaluationRow
    {
        public long Queries { get; set; }
        public long Top1Correct { get; set; }
        public long Top3Correct { get; set; }
        public long Ambiguous { get; set; }
        public long AmbiguousTop1Correct { get; set; }
    }
}
