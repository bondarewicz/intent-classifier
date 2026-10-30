namespace IntentClassifier.Infrastructure.Persistence;

using Dapper;
using Domain.Intents;
using Domain.Queries;
using Npgsql;
using Pgvector;

internal sealed class LabelledQueryRepository(NpgsqlDataSource dataSource)
    : ILabelledQueryRepository
{
    // Prefers a row that already has an embedding when the text exists in both splits.
    public async Task<LabelledQuery?> FindByText(QueryText text, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<LabelledQueryRow>(
            new CommandDefinition(
                $"""
                SELECT query_id, split, query_text, intent_id, embedding
                FROM {Schema.LabelledQueries}
                WHERE query_text = @QueryText
                ORDER BY embedding IS NULL
                LIMIT 1
                """,
                new { QueryText = text.Value },
                cancellationToken: ct
            )
        );
        return row is null ? null : ToLabelledQuery(row);
    }

    public async Task<IReadOnlyList<LabelledQuery>> ListWithoutEmbedding(
        CancellationToken ct = default
    )
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<LabelledQueryRow>(
            new CommandDefinition(
                $"""
                SELECT query_id, split, query_text, intent_id
                FROM {Schema.LabelledQueries}
                WHERE embedding IS NULL
                ORDER BY split, query_text
                """,
                cancellationToken: ct
            )
        );
        return rows.Select(ToLabelledQuery).ToList();
    }

    // One statement per batch: ids and vectors as two parallel arrays, unnested and joined.
    public async Task SaveEmbeddings(
        IReadOnlyCollection<LabelledQuery> queries,
        CancellationToken ct = default
    )
    {
        var embedded = queries.Where(q => q.HasEmbedding).ToList();
        if (embedded.Count == 0)
            return;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            new CommandDefinition(
                $"""
                UPDATE {Schema.LabelledQueries} q
                SET embedding = u.embedding::vector,
                    embedded_at = now() AT TIME ZONE 'utc'
                FROM unnest(@QueryIds, @Embeddings) AS u(query_id, embedding)
                WHERE q.query_id = u.query_id
                """,
                new
                {
                    QueryIds = embedded.Select(q => q.Id).ToArray(),
                    Embeddings = embedded.Select(q => q.Embedding!.ToVectorLiteral()).ToArray(),
                },
                cancellationToken: ct
            )
        );
    }

    private static LabelledQuery ToLabelledQuery(LabelledQueryRow row) =>
        new(
            row.QueryId,
            QueryText.Create(row.QueryText),
            row.Split.ToDatasetSplit(),
            IntentId.Create(row.IntentId),
            row.Embedding?.ToEmbedding()
        );

    private sealed class LabelledQueryRow
    {
        public Guid QueryId { get; set; }
        public string Split { get; set; } = "";
        public string QueryText { get; set; } = "";
        public string IntentId { get; set; } = "";
        public Vector? Embedding { get; set; }
    }
}
