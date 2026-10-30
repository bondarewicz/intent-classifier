namespace IntentClassifier.Infrastructure.Persistence.Intents;

using Domain.Intents;
using LabelledQueries;

internal static class IntentsSchema
{
    public const string Table = "intents";

    public static string EmbeddingColumn(IntentRepresentation representation) =>
        representation switch
        {
            IntentRepresentation.Centroid => "centroid_embedding",
            IntentRepresentation.Name => "name_embedding",
            _ => throw new ArgumentOutOfRangeException(
                nameof(representation),
                representation,
                null
            ),
        };

    public static class Sql
    {
        public static readonly string Exists = $"""
            SELECT EXISTS (SELECT 1 FROM {Table} WHERE intent_id = @IntentId)
            """;

        public static readonly string SelectWithoutNameEmbedding = $"""
            SELECT intent_id, intent_name
            FROM {Table}
            WHERE name_embedding IS NULL
            ORDER BY intent_id
            """;

        public static readonly string UpdateNameEmbeddings = $"""
            UPDATE {Table} i
            SET name_embedding = u.embedding::vector
            FROM unnest(@IntentIds, @Embeddings) AS u(intent_id, embedding)
            WHERE i.intent_id = u.intent_id
            """;

        public static readonly string InsertMissing = $"""
            INSERT INTO {Table} (intent_id, intent_name)
            SELECT * FROM unnest(@IntentIds, @IntentNames)
            ON CONFLICT (intent_id) DO NOTHING
            """;

        public static readonly string UpdateCentroids = $"""
            UPDATE {Table} i
            SET centroid_embedding = c.centroid,
                example_count = c.examples,
                centroid_updated_at = now() AT TIME ZONE 'utc'
            FROM (
                SELECT intent_id, avg(embedding) AS centroid, count(*) AS examples
                FROM {LabelledQueriesSchema.Table}
                WHERE split = 'train' AND embedding IS NOT NULL
                GROUP BY intent_id
            ) c
            WHERE i.intent_id = c.intent_id
            """;

        // Exact search with pgvector's cosine distance operator; similarity = 1 - distance.
        public static string SelectClosest(IntentRepresentation representation)
        {
            var embeddingColumn = EmbeddingColumn(representation);
            var distance = $"{embeddingColumn} <=> @Embedding";
            return $"""
                SELECT row_number() OVER (ORDER BY {distance})::int AS rank,
                       intent_id,
                       intent_name,
                       1 - ({distance}) AS similarity
                FROM {Table}
                WHERE {embeddingColumn} IS NOT NULL
                ORDER BY {distance}
                LIMIT @Top
                """;
        }
    }
}
