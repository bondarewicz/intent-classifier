namespace IntentClassifier.Infrastructure.Persistence;

using Application.Queries;
using Dapper;
using Domain.Embeddings;
using Domain.Intents;
using Domain.Queries;
using Npgsql;
using Pgvector;

internal sealed class ClassifiedQueryLog(NpgsqlDataSource dataSource) : IClassifiedQueryLog
{
    public async Task<Embedding?> FindEmbedding(QueryText text, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var embedding = await conn.QuerySingleOrDefaultAsync<Vector>(
            new CommandDefinition(
                $"SELECT embedding FROM {Schema.ClassifiedQueries} WHERE query_text = @QueryText",
                new { QueryText = text.Value },
                cancellationToken: ct
            )
        );
        return embedding?.ToEmbedding();
    }

    // The embedding is only ever set on insert; a repeat only refreshes the rest.
    public async Task Record(
        QueryText text,
        Embedding embedding,
        IntentRepresentation representation,
        IntentId? bestIntentId,
        double? bestSimilarity,
        bool ambiguous,
        CancellationToken ct = default
    )
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            new CommandDefinition(
                $"""
                INSERT INTO {Schema.ClassifiedQueries} (
                    query_text, embedding, representation, best_intent_id, best_similarity, ambiguous,
                    first_classified_at, last_classified_at
                )
                VALUES (
                    @QueryText, @Embedding, @Representation, @BestIntentId, @BestSimilarity, @Ambiguous,
                    now() AT TIME ZONE 'utc', now() AT TIME ZONE 'utc'
                )
                ON CONFLICT (query_text) DO UPDATE SET
                    representation = EXCLUDED.representation,
                    best_intent_id = EXCLUDED.best_intent_id,
                    best_similarity = EXCLUDED.best_similarity,
                    ambiguous = EXCLUDED.ambiguous,
                    times_classified = {Schema.ClassifiedQueries}.times_classified + 1,
                    last_classified_at = EXCLUDED.last_classified_at
                """,
                new
                {
                    QueryText = text.Value,
                    Embedding = embedding.ToVector(),
                    Representation = representation.ToString(),
                    BestIntentId = bestIntentId?.Value,
                    BestSimilarity = bestSimilarity,
                    Ambiguous = ambiguous,
                },
                cancellationToken: ct
            )
        );
    }

    // Guards and both writes share one transaction holding a lock on the log row, so two label
    // calls for the same text run one after the other instead of racing.
    public async Task Confirm(QueryText text, IntentId intentId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // FOR UPDATE holds the row for the rest of the transaction, so a concurrent classify (whose
        // upsert would block on the same row anyway) or a second label call cannot race the guards.
        var logged = await conn.QuerySingleOrDefaultAsync<ClassifiedQueryRow>(
            new CommandDefinition(
                $"SELECT embedding, confirmed_intent_id FROM {Schema.ClassifiedQueries} WHERE query_text = @QueryText FOR UPDATE",
                new { QueryText = text.Value },
                tx,
                cancellationToken: ct
            )
        );
        if (logged is null)
            throw new InvalidQueryException(
                $"'{text}' has not been classified yet - run classify first."
            );

        var intentExists = await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                $"SELECT EXISTS (SELECT 1 FROM {Schema.Intents} WHERE intent_id = @IntentId)",
                new { IntentId = intentId.Value },
                tx,
                cancellationToken: ct
            )
        );
        if (!intentExists)
            throw new InvalidQueryException($"Intent '{intentId}' does not exist.");

        if (await IsLabelled(conn, tx, "test", text, ct))
            throw new InvalidQueryException(
                $"'{text}' is in the test split - labelling it would leak test data into training."
            );

        // Not confirmed before, so an existing train row is original dataset data: never overwrite.
        if (logged.ConfirmedIntentId is null && await IsLabelled(conn, tx, "train", text, ct))
            throw new InvalidQueryException($"'{text}' is already labelled in the dataset.");

        await conn.ExecuteAsync(
            new CommandDefinition(
                $"""
                INSERT INTO {Schema.LabelledQueries} (split, query_text, intent_id, embedding, embedded_at)
                VALUES ('train', @QueryText, @IntentId, @Embedding, now() AT TIME ZONE 'utc')
                ON CONFLICT (split, query_text) DO UPDATE SET
                    intent_id = EXCLUDED.intent_id,
                    embedding = EXCLUDED.embedding,
                    embedded_at = EXCLUDED.embedded_at
                """,
                new
                {
                    QueryText = text.Value,
                    IntentId = intentId.Value,
                    Embedding = logged.Embedding,
                },
                tx,
                cancellationToken: ct
            )
        );
        var confirmed = await conn.ExecuteAsync(
            new CommandDefinition(
                $"""
                UPDATE {Schema.ClassifiedQueries}
                SET confirmed_intent_id = @IntentId, confirmed_at = now() AT TIME ZONE 'utc'
                WHERE query_text = @QueryText
                """,
                new { QueryText = text.Value, IntentId = intentId.Value },
                tx,
                cancellationToken: ct
            )
        );
        if (confirmed != 1)
            throw new InvalidOperationException(
                $"Expected to confirm exactly one classified_queries row for '{text}', affected {confirmed}."
            );

        await tx.CommitAsync(ct);
    }

    private static Task<bool> IsLabelled(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string split,
        QueryText text,
        CancellationToken ct
    ) =>
        conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                $"SELECT EXISTS (SELECT 1 FROM {Schema.LabelledQueries} WHERE split = @Split AND query_text = @QueryText)",
                new { Split = split, QueryText = text.Value },
                tx,
                cancellationToken: ct
            )
        );

    private sealed class ClassifiedQueryRow
    {
        public Vector Embedding { get; set; } = null!;
        public string? ConfirmedIntentId { get; set; }
    }
}
