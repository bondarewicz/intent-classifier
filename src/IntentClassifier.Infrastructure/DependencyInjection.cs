namespace IntentClassifier.Infrastructure;

using IntentClassifier.Application.Abstractions;
using IntentClassifier.Application.Classification;
using IntentClassifier.Application.Evaluation;
using IntentClassifier.Application.Queries;
using IntentClassifier.Domain.Intents;
using IntentClassifier.Domain.Queries;
using IntentClassifier.Infrastructure.Embeddings;
using IntentClassifier.Infrastructure.Persistence;
using IntentClassifier.Infrastructure.Persistence.ClassifiedQueries;
using IntentClassifier.Infrastructure.Persistence.Import;
using IntentClassifier.Infrastructure.Persistence.IntentCandidates;
using IntentClassifier.Infrastructure.Persistence.Intents;
using IntentClassifier.Infrastructure.Persistence.LabelledQueries;
using IntentClassifier.Infrastructure.Persistence.Reports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        DapperConfiguration.EnsureRegistered();

        // No-op under a host builder, which registers its own; needed for the tests' raw container.
        services.TryAddSingleton(configuration);

        // Lazy, so a test host can still override ConnectionStrings:Postgres after this runs.
        services.AddSingleton(sp =>
        {
            var connectionString = ReadPostgresConnectionString(
                sp.GetRequiredService<IConfiguration>()
            );
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.UseVector();
            return builder.Build();
        });

        services.AddSingleton<IIntentRepository, IntentRepository>();
        services.AddSingleton<ILabelledQueryRepository, LabelledQueryRepository>();
        services.AddSingleton<IClassifiedQueryLog, ClassifiedQueryLog>();
        services.AddSingleton<IIntentCandidateProjection, IntentCandidateProjection>();
        services.AddSingleton<ClassificationReportQueries>();
        services.AddSingleton<IClassificationReportQueries>(sp =>
            sp.GetRequiredService<ClassificationReportQueries>()
        );
        services.AddSingleton<IEvaluationQueries>(sp =>
            sp.GetRequiredService<ClassificationReportQueries>()
        );
        services.AddSingleton<IDatasetImporter, Banking77Importer>();

        DatabaseMigrator.AddTo(services);
        services.AddSingleton<IDatabaseMigrator, DatabaseMigrator>();

        services
            .AddOptions<OpenAiOptions>()
            .Bind(configuration.GetSection(OpenAiOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Model), "OpenAi:Model must be set.")
            .ValidateOnStart();
        services.AddSingleton<IEmbeddingGenerator, OpenAiEmbeddingGenerator>();

        return services;
    }

    // internal: DatabaseMigrator.AddTo resolves the same connection string the same way.
    internal static string ReadPostgresConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new MissingConfigurationException(
                "ConnectionStrings:Postgres is not configured."
            )
            : connectionString;
    }
}
