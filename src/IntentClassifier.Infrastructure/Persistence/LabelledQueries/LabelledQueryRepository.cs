namespace IntentClassifier.Infrastructure.Persistence.LabelledQueries;

using Dapper;
using Domain.Intents;
using Domain.Queries;
using Npgsql;

internal sealed class LabelledQueryRepository(NpgsqlDataSource dataSource)
    : ILabelledQueryRepository
{
    public async Task<LabelledQuery?> FindByText(QueryText text, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<LabelledQueryRow>(
            new CommandDefinition(
                LabelledQueriesSchema.Sql.SelectByText,
                new { QueryText = text.Value },
                cancellationToken: ct
            )
        );
        return row?.MapToLabelledQuery();
    }

    public async Task<IReadOnlyList<LabelledQuery>> ListWithoutEmbedding(
        CancellationToken ct = default
    )
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<LabelledQueryRow>(
            new CommandDefinition(
                LabelledQueriesSchema.Sql.SelectWithoutEmbedding,
                cancellationToken: ct
            )
        );
        return rows.Select(r => r.MapToLabelledQuery()).ToList();
    }

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
                LabelledQueriesSchema.Sql.UpdateEmbeddings,
                new
                {
                    QueryIds = embedded.Select(q => q.Id).ToArray(),
                    Embeddings = embedded.Select(q => q.Embedding!.ToVectorLiteral()).ToArray(),
                },
                cancellationToken: ct
            )
        );
    }
}

internal static class LabelledQueryRowExtensions
{
    public static LabelledQuery MapToLabelledQuery(this LabelledQueryRow row) =>
        new(
            row.QueryId,
            QueryText.Create(row.QueryText),
            row.Split.ToDatasetSplit(),
            IntentId.Create(row.IntentId),
            row.Embedding?.ToEmbedding()
        );
}
