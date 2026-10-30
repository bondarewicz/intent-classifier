namespace IntentClassifier.Infrastructure.Persistence.IntentCandidates;

using Domain.Intents;
using Intents;
using LabelledQueries;

internal static class QueryIntentCandidatesSchema
{
    public const string Table = "query_intent_candidates";

    public static class Sql
    {
        // DELETE, not TRUNCATE: TRUNCATE takes ACCESS EXCLUSIVE and would block the report view
        // and the API for the whole recompute; DELETE lets concurrent readers see the old rows
        // under MVCC until this transaction commits.
        public static readonly string DeleteAll = $"DELETE FROM {Table};";

        public static readonly string Count = $"SELECT count(*) FROM {Table}";

        // Set-based: pgvector computes queries x intents cosine distances in one statement.
        public static string InsertTopCandidates(IntentRepresentation representation)
        {
            var intentEmbedding = $"i.{IntentsSchema.EmbeddingColumn(representation)}";
            var distance = $"{intentEmbedding} <=> q.embedding";
            return $"""
                INSERT INTO {Table} (query_id, rank, intent_id, similarity)
                SELECT q.query_id, m.rank, m.intent_id, m.similarity
                FROM {LabelledQueriesSchema.Table} q
                CROSS JOIN LATERAL (
                    SELECT i.intent_id,
                           1 - ({distance}) AS similarity,
                           row_number() OVER (ORDER BY {distance})::smallint AS rank
                    FROM {IntentsSchema.Table} i
                    WHERE {intentEmbedding} IS NOT NULL
                    ORDER BY {distance}
                    LIMIT @Top
                ) m
                WHERE q.embedding IS NOT NULL;
                """;
        }
    }
}
