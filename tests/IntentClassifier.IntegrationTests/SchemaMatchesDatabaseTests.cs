namespace IntentClassifier.IntegrationTests;

using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using IntentClassifier.Infrastructure.Persistence.ClassifiedQueries;
using IntentClassifier.Infrastructure.Persistence.IntentCandidates;
using IntentClassifier.Infrastructure.Persistence.Intents;
using IntentClassifier.Infrastructure.Persistence.LabelledQueries;
using IntentClassifier.Infrastructure.Persistence.Reports;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// Nothing else links a Row class's property names to the migration that actually creates the
// columns they read; this is that link. A Row property rename with no matching migration change
// fails here instead of in whichever SQL happens to run first.
public sealed class SchemaMatchesDatabaseTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Theory]
    [MemberData(nameof(SchemaTables))]
    public async Task Row_properties_exist_as_columns(string table, Type rowType)
    {
        var dataSource = db.Services!.GetRequiredService<NpgsqlDataSource>();
        await using var conn = await dataSource.OpenConnectionAsync();
        var actual = (
            await conn.QueryAsync<string>(
                "SELECT column_name FROM information_schema.columns WHERE table_name = @Table",
                new { Table = table }
            )
        ).ToHashSet();

        var expected = rowType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => ToSnakeCase(p.Name));

        Assert.All(expected, column => Assert.Contains(column, actual));
    }

    public static IEnumerable<object[]> SchemaTables()
    {
        yield return [IntentsSchema.Table, typeof(IntentRow)];
        yield return [LabelledQueriesSchema.Table, typeof(LabelledQueryRow)];
        yield return [QueryIntentCandidatesSchema.Table, typeof(QueryIntentCandidateRow)];
        yield return [QueryClassificationReportSchema.View, typeof(QueryClassificationReportRow)];
        yield return [ClassifiedQueriesSchema.Table, typeof(ClassifiedQueryRow)];
    }

    private static string ToSnakeCase(string pascalCase) =>
        Regex.Replace(pascalCase, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
}
