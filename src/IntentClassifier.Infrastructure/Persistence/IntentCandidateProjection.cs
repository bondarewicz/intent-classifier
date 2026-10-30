namespace IntentClassifier.Infrastructure.Persistence;

using Application.Classification;
using Dapper;
using Domain.Intents;
using Npgsql;

internal sealed class IntentCandidateProjection(NpgsqlDataSource dataSource)
    : IIntentCandidateProjection
{
    // The new results replace the previous run atomically.
    public async Task<long> RebuildAll(
        int top,
        IntentRepresentation representation,
        CancellationToken ct = default
    )
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // DELETE, not TRUNCATE: TRUNCATE takes ACCESS EXCLUSIVE and would block the report view
        // and the API for the whole recompute; DELETE lets concurrent readers see the old rows
        // under MVCC until this transaction commits.
        await conn.ExecuteAsync(
            new CommandDefinition(
                $"DELETE FROM {Schema.QueryIntentCandidates}",
                transaction: tx,
                cancellationToken: ct
            )
        );
        await conn.ExecuteAsync(
            new CommandDefinition(
                InsertTopCandidates(representation),
                new { Top = top },
                tx,
                commandTimeout: 0,
                cancellationToken: ct
            )
        );
        var stored = await conn.ExecuteScalarAsync<long>(
            new CommandDefinition(
                $"SELECT count(*) FROM {Schema.QueryIntentCandidates}",
                transaction: tx,
                cancellationToken: ct
            )
        );

        await tx.CommitAsync(ct);
        return stored;
    }

    // Set-based: pgvector computes queries x intents cosine distances in one statement.
    private static string InsertTopCandidates(IntentRepresentation representation)
    {
        var intentEmbedding = $"i.{Schema.EmbeddingColumn(representation)}";
        var distance = $"{intentEmbedding} <=> q.embedding";
        return $"""
            INSERT INTO {Schema.QueryIntentCandidates} (query_id, rank, intent_id, similarity)
            SELECT q.query_id, m.rank, m.intent_id, m.similarity
            FROM {Schema.LabelledQueries} q
            CROSS JOIN LATERAL (
                SELECT i.intent_id,
                       1 - ({distance}) AS similarity,
                       row_number() OVER (ORDER BY {distance})::smallint AS rank
                FROM {Schema.Intents} i
                WHERE {intentEmbedding} IS NOT NULL
                ORDER BY {distance}
                LIMIT @Top
            ) m
            WHERE q.embedding IS NOT NULL
            """;
    }
}
