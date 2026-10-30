namespace IntentClassifier.Infrastructure.Persistence;

using Dapper;
using Domain.Classification;
using Domain.Embeddings;
using Domain.Intents;
using Npgsql;

internal sealed class IntentRepository(NpgsqlDataSource dataSource) : IIntentRepository
{
    public async Task<IReadOnlyList<Intent>> ListWithoutNameEmbedding(
        CancellationToken ct = default
    )
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<IntentRow>(
            new CommandDefinition(
                $"""
                SELECT intent_id, intent_name
                FROM {Schema.Intents}
                WHERE name_embedding IS NULL
                ORDER BY intent_id
                """,
                cancellationToken: ct
            )
        );
        return rows.Select(r => new Intent(IntentId.Create(r.IntentId), r.IntentName)).ToList();
    }

    public async Task SaveNameEmbeddings(
        IReadOnlyCollection<Intent> intents,
        CancellationToken ct = default
    )
    {
        var embedded = intents.Where(i => i.HasNameEmbedding).ToList();
        if (embedded.Count == 0)
            return;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            new CommandDefinition(
                $"""
                UPDATE {Schema.Intents} i
                SET name_embedding = u.embedding::vector
                FROM unnest(@IntentIds, @Embeddings) AS u(intent_id, embedding)
                WHERE i.intent_id = u.intent_id
                """,
                new
                {
                    IntentIds = embedded.Select(i => i.Id.Value).ToArray(),
                    Embeddings = embedded.Select(i => i.NameEmbedding!.ToVectorLiteral()).ToArray(),
                },
                cancellationToken: ct
            )
        );
    }

    public async Task<int> RecomputeCentroids(CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(
            new CommandDefinition(
                $"""
                UPDATE {Schema.Intents} i
                SET centroid_embedding = c.centroid,
                    example_count = c.examples,
                    centroid_updated_at = now() AT TIME ZONE 'utc'
                FROM (
                    SELECT intent_id, avg(embedding) AS centroid, count(*) AS examples
                    FROM {Schema.LabelledQueries}
                    WHERE split = 'train' AND embedding IS NOT NULL
                    GROUP BY intent_id
                ) c
                WHERE i.intent_id = c.intent_id
                """,
                cancellationToken: ct
            )
        );
    }

    // Exact search with pgvector's cosine distance operator; similarity = 1 - distance.
    public async Task<IReadOnlyList<IntentCandidate>> FindClosest(
        Embedding embedding,
        int top,
        IntentRepresentation representation,
        CancellationToken ct = default
    )
    {
        var column = Schema.EmbeddingColumn(representation);
        var distance = $"{column} <=> @Embedding";

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<IntentCandidateRow>(
            new CommandDefinition(
                $"""
                SELECT row_number() OVER (ORDER BY {distance})::int AS rank,
                       intent_id,
                       intent_name,
                       1 - ({distance}) AS similarity
                FROM {Schema.Intents}
                WHERE {column} IS NOT NULL
                ORDER BY {distance}
                LIMIT @Top
                """,
                new { Embedding = embedding.ToVector(), Top = top },
                cancellationToken: ct
            )
        );
        return rows.Select(r => new IntentCandidate(
                r.Rank,
                IntentId.Create(r.IntentId),
                r.IntentName,
                r.Similarity
            ))
            .ToList();
    }

    private sealed class IntentRow
    {
        public string IntentId { get; set; } = "";
        public string IntentName { get; set; } = "";
    }

    private sealed class IntentCandidateRow
    {
        public int Rank { get; set; }
        public string IntentId { get; set; } = "";
        public string IntentName { get; set; } = "";
        public double Similarity { get; set; }
    }
}
