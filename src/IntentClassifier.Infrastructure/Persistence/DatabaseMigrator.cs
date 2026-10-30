namespace IntentClassifier.Infrastructure.Persistence;

using FluentMigrator.Runner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Migrations;

public sealed class DatabaseMigrator(IServiceScopeFactory scopes)
{
    public void MigrateUp()
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }

    // Connection string read per scope, so a test host's configuration override still applies.
    public static void AddTo(IServiceCollection services) =>
        services
            .AddFluentMigratorCore()
            .ConfigureRunner(rb =>
                rb.AddPostgres()
                    .WithGlobalConnectionString(sp =>
                        DependencyInjection.ReadPostgresConnectionString(
                            sp.GetRequiredService<IConfiguration>()
                        )
                    )
                    .ScanIn(typeof(EnableVectorExtension).Assembly)
                    .For.Migrations()
            );
}
