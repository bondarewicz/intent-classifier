namespace IntentClassifier.Infrastructure.Persistence.IntentCandidates;

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

        await conn.ExecuteAsync(
            new CommandDefinition(
                QueryIntentCandidatesSchema.Sql.DeleteAll,
                transaction: tx,
                cancellationToken: ct
            )
        );
        await conn.ExecuteAsync(
            new CommandDefinition(
                QueryIntentCandidatesSchema.Sql.InsertTopCandidates(representation),
                new { Top = top },
                tx,
                commandTimeout: 0,
                cancellationToken: ct
            )
        );
        var stored = await conn.ExecuteScalarAsync<long>(
            new CommandDefinition(
                QueryIntentCandidatesSchema.Sql.Count,
                transaction: tx,
                cancellationToken: ct
            )
        );

        await tx.CommitAsync(ct);
        return stored;
    }
}
