namespace IntentClassifier.IntegrationTests;

using Application;
using Application.Abstractions;
using Application.Embeddings;
using Application.Intents;
using Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

// One throwaway pgvector Postgres per test class, migrated and seeded (migrate, seed, embed,
// train) with a tiny Banking77-shaped dataset, so every test class sharing this fixture starts
// from the same ready-to-classify state.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("banking77_test")
        .Build();

    public ServiceProvider? Services { get; private set; }

    public string ConnectionString => container.GetConnectionString();

    public string DataDirectory { get; } =
        Path.Combine(Path.GetTempPath(), $"intentclassifier-{Guid.NewGuid():N}");

    public string TrainCsv => Path.Combine(DataDirectory, "train.csv");

    public string TestCsv => Path.Combine(DataDirectory, "test.csv");

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = ConnectionString,
                }
            )
            .Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddInfrastructure(configuration)
            .AddSingleton<IEmbeddingGenerator, HashingEmbeddingGenerator>() // replaces OpenAI
            .BuildServiceProvider();
        Services = services;

        Directory.CreateDirectory(DataDirectory);
        await File.WriteAllTextAsync(TrainCsv, Banking77TestData.Train);
        await File.WriteAllTextAsync(TestCsv, Banking77TestData.Test);

        services.GetRequiredService<IDatabaseMigrator>().MigrateUp();
        await services.GetRequiredService<IDatasetImporter>().ImportBanking77(TrainCsv, TestCsv);
        await services.GetRequiredService<EmbedDatasetHandler>().Handle(batchSize: 4);
        await services.GetRequiredService<TrainIntentsHandler>().Handle();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (Services is not null)
                await Services.DisposeAsync();
        }
        finally
        {
            try
            {
                await container.DisposeAsync();
            }
            finally
            {
                if (Directory.Exists(DataDirectory))
                    Directory.Delete(DataDirectory, recursive: true);
            }
        }
    }
}
