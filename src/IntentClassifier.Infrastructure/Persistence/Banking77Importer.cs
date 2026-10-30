namespace IntentClassifier.Infrastructure.Persistence;

using Dapper;
using Domain.Intents;
using Domain.Queries;
using Npgsql;

public sealed record DatasetImportResult(long Intents, long TrainQueries, long TestQueries);

// Loads the Banking77 CSVs (text,category): streams them into a temp stage with COPY FROM STDIN
// (client side, nothing mounted into the container), normalizes and validates every row with the
// domain types, then inserts what is missing. Re-runnable: existing intents and queries (and their
// embeddings) are kept.
public sealed class Banking77Importer(NpgsqlDataSource dataSource)
{
    private const string Stage = "banking77_stage";

    public async Task<DatasetImportResult> ImportBanking77(
        string trainCsvPath,
        string testCsvPath,
        CancellationToken ct = default
    )
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var staged = await StageCsvs(conn, tx, trainCsvPath, testCsvPath, ct);

        // One pass: canonicalize each staged row once instead of once per intent list and once
        // per query list.
        var rows = staged
            .Select(r =>
                (
                    Split: r.Split,
                    Text: QueryText.Create(r.QueryText).Value,
                    Intent: IntentId.Create(r.Category)
                )
            )
            .ToList();
        var intents = rows.Select(r => r.Intent)
            .Distinct()
            .OrderBy(i => i.Value, StringComparer.Ordinal)
            .ToList();
        // Duplicate texts within a split collapse to one row (first wins).
        var queries = rows.DistinctBy(r => (r.Split, r.Text)).ToList();

        await InsertMissing(conn, tx, intents, queries, ct);
        var counts = await CountBySplit(conn, tx, ct);

        await conn.ExecuteAsync(
            new CommandDefinition(
                $"ANALYZE {Schema.Intents}; ANALYZE {Schema.LabelledQueries};",
                transaction: tx,
                cancellationToken: ct
            )
        );
        await tx.CommitAsync(ct);

        return new DatasetImportResult(
            intents.Count,
            counts.GetValueOrDefault(DatasetSplit.Train.ToDb()),
            counts.GetValueOrDefault(DatasetSplit.Test.ToDb())
        );
    }

    private static async Task<List<StagedRow>> StageCsvs(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string trainCsvPath,
        string testCsvPath,
        CancellationToken ct
    )
    {
        await conn.ExecuteAsync(
            new CommandDefinition(
                $"CREATE TEMP TABLE {Stage} (query_text text, category text, split text) ON COMMIT DROP",
                transaction: tx,
                cancellationToken: ct
            )
        );
        await CopyIntoStage(conn, tx, trainCsvPath, DatasetSplit.Train, ct);
        await CopyIntoStage(conn, tx, testCsvPath, DatasetSplit.Test, ct);

        return (
            await conn.QueryAsync<StagedRow>(
                new CommandDefinition(
                    $"SELECT query_text, category, split FROM {Stage}",
                    transaction: tx,
                    cancellationToken: ct
                )
            )
        ).ToList();
    }

    private static async Task CopyIntoStage(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string csvPath,
        DatasetSplit split,
        CancellationToken ct
    )
    {
        await using (
            var writer = await conn.BeginTextImportAsync(
                $"COPY {Stage} (query_text, category) FROM STDIN (FORMAT csv, HEADER true)",
                ct
            )
        )
        using (var reader = File.OpenText(csvPath))
        {
            var buffer = new char[81920];
            int read;
            while ((read = await reader.ReadAsync(buffer, ct)) > 0)
                await writer.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        // Tags the rows just COPYed, before the next file lands in the same stage.
        await conn.ExecuteAsync(
            new CommandDefinition(
                $"UPDATE {Stage} SET split = @Split WHERE split IS NULL",
                new { Split = split.ToDb() },
                tx,
                cancellationToken: ct
            )
        );
    }

    private static async Task InsertMissing(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        IReadOnlyCollection<IntentId> intents,
        IReadOnlyCollection<(string Split, string Text, IntentId Intent)> queries,
        CancellationToken ct
    )
    {
        await conn.ExecuteAsync(
            new CommandDefinition(
                $"""
                INSERT INTO {Schema.Intents} (intent_id, intent_name)
                SELECT * FROM unnest(@IntentIds, @IntentNames)
                ON CONFLICT (intent_id) DO NOTHING
                """,
                new
                {
                    IntentIds = intents.Select(i => i.Value).ToArray(),
                    IntentNames = intents.Select(i => i.ToDisplayName()).ToArray(),
                },
                tx,
                cancellationToken: ct
            )
        );

        await conn.ExecuteAsync(
            new CommandDefinition(
                $"""
                INSERT INTO {Schema.LabelledQueries} (split, query_text, intent_id)
                SELECT * FROM unnest(@Splits, @QueryTexts, @IntentIds)
                ON CONFLICT (split, query_text) DO NOTHING
                """,
                new
                {
                    Splits = queries.Select(q => q.Split).ToArray(),
                    QueryTexts = queries.Select(q => q.Text).ToArray(),
                    IntentIds = queries.Select(q => q.Intent.Value).ToArray(),
                },
                tx,
                cancellationToken: ct
            )
        );
    }

    private static async Task<Dictionary<string, long>> CountBySplit(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CancellationToken ct
    ) =>
        (
            await conn.QueryAsync<(string Split, long Count)>(
                new CommandDefinition(
                    $"SELECT split, count(*) FROM {Schema.LabelledQueries} GROUP BY split",
                    transaction: tx,
                    cancellationToken: ct
                )
            )
        ).ToDictionary(c => c.Split, c => c.Count);

    private sealed class StagedRow
    {
        public string QueryText { get; set; } = "";
        public string Category { get; set; } = "";
        public string Split { get; set; } = "";
    }
}
