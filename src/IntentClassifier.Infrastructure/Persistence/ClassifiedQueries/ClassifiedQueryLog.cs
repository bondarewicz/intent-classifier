namespace IntentClassifier.Infrastructure.Persistence.ClassifiedQueries;

using Application.Queries;
using Dapper;
using Domain.Embeddings;
using Domain.Intents;
using Domain.Queries;
using Intents;
using LabelledQueries;
using Npgsql;
using Pgvector;

internal sealed class ClassifiedQueryLog(NpgsqlDataSource dataSource) : IClassifiedQueryLog
{
    public async Task<Embedding?> FindEmbedding(QueryText text, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var embedding = await conn.QuerySingleOrDefaultAsync<Vector>(
            new CommandDefinition(
                ClassifiedQueriesSchema.Sql.SelectEmbeddingByText,
                new { QueryText = text.Value },
                cancellationToken: ct
            )
        );
        return embedding?.ToEmbedding();
    }

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
                ClassifiedQueriesSchema.Sql.Upsert,
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

        var logged = await conn.QuerySingleOrDefaultAsync<ClassifiedQueryRow>(
            new CommandDefinition(
                ClassifiedQueriesSchema.Sql.SelectForConfirmUpdate,
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
                IntentsSchema.Sql.Exists,
                new { IntentId = intentId.Value },
                tx,
                cancellationToken: ct
            )
        );
        if (!intentExists)
            throw new InvalidQueryException($"Intent '{intentId}' does not exist.");

        var inTestSplit = await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                LabelledQueriesSchema.Sql.ExistsBySplitAndText,
                new { Split = "test", QueryText = text.Value },
                tx,
                cancellationToken: ct
            )
        );
        if (inTestSplit)
            throw new InvalidQueryException(
                $"'{text}' is in the test split - labelling it would leak test data into training."
            );

        // Not confirmed before, so an existing train row is original dataset data: never overwrite.
        if (logged.ConfirmedIntentId is null)
        {
            var inTrainSplit = await conn.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    LabelledQueriesSchema.Sql.ExistsBySplitAndText,
                    new { Split = "train", QueryText = text.Value },
                    tx,
                    cancellationToken: ct
                )
            );
            if (inTrainSplit)
                throw new InvalidQueryException($"'{text}' is already labelled in the dataset.");
        }

        await conn.ExecuteAsync(
            new CommandDefinition(
                LabelledQueriesSchema.Sql.UpsertTrainExample,
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
                ClassifiedQueriesSchema.Sql.ConfirmPromotion,
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
}
