namespace IntentClassifier.Infrastructure.Persistence.Reports;

using IntentClassifier.Domain.Intents;
using IntentClassifier.Infrastructure.Persistence.Intents;
using IntentClassifier.Infrastructure.Persistence.LabelledQueries;

internal static class QueryClassificationReportSchema
{
    public const string View = "query_classification_report";

    public static class Sql
    {
        // Train before test, explicitly - not relying on the alphabetical sort of the split strings.
        public static readonly string SelectSummaryBySplit = $"""
            SELECT split,
                   count(*) AS queries,
                   coalesce(avg(best_similarity), 0)::double precision AS average_best_similarity,
                   count(*) FILTER (WHERE is_correct) AS correct,
                   count(*) FILTER (WHERE margin < @Margin) AS ambiguous
            FROM {View}
            GROUP BY split
            ORDER BY (split = 'train') DESC
            """;

        // Classifies every embedded query of a split against the intents on the fly (top @Top) and
        // compares with the labelled intent; nothing is stored.
        public static string Evaluate(IntentRepresentation representation)
        {
            var intentEmbedding = $"i.{IntentsSchema.EmbeddingColumn(representation)}";
            var distance = $"{intentEmbedding} <=> q.embedding";
            return $"""
                WITH ranked AS (
                    SELECT q.query_id, q.intent_id AS expected,
                           m.intent_id, m.rank, m.similarity
                    FROM {LabelledQueriesSchema.Table} q
                    CROSS JOIN LATERAL (
                        SELECT i.intent_id,
                               1 - ({distance}) AS similarity,
                               row_number() OVER (ORDER BY {distance}) AS rank
                        FROM {IntentsSchema.Table} i
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
    }
}
