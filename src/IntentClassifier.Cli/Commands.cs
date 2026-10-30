namespace IntentClassifier.Cli;

using Application.Classification;
using Application.Embeddings;
using Application.Evaluation;
using Application.Intents;
using Application.Queries;
using Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

internal static class Commands
{
    public static int Migrate(IServiceProvider services)
    {
        services.GetRequiredService<DatabaseMigrator>().MigrateUp();
        Console.WriteLine("Database is at the latest migration.");
        return 0;
    }

    public static async Task<int> Seed(IServiceProvider services, CliOptions options)
    {
        var result = await services
            .GetRequiredService<Banking77Importer>()
            .ImportBanking77(options.TrainPath, options.TestPath);
        Console.WriteLine(
            $"Intents: {result.Intents}, train queries: {result.TrainQueries:N0}, test queries: {result.TestQueries:N0}"
        );
        return 0;
    }

    public static async Task<int> Embed(IServiceProvider services, CliOptions options)
    {
        var result = await services
            .GetRequiredService<EmbedDatasetHandler>()
            .Handle(
                options.BatchSize,
                new ConsoleProgress<EmbedDatasetProgress>(p =>
                    Console.WriteLine($"  {p.Stage}: {p.Embedded}/{p.Total}")
                )
            );
        Console.WriteLine(
            $"Embedded {result.IntentNames} intent names and {result.Queries:N0} queries."
        );
        return 0;
    }

    public static async Task<int> Train(IServiceProvider services)
    {
        var intents = await services.GetRequiredService<TrainIntentsHandler>().Handle();
        Console.WriteLine($"Recomputed centroids for {intents} intents.");
        return 0;
    }

    public static async Task<int> ClassifyAll(IServiceProvider services, CliOptions options)
    {
        var top = options.Top ?? 5;
        var started = DateTime.UtcNow;
        var result = await services
            .GetRequiredService<ClassifyAllHandler>()
            .Handle(top, options.Representation);

        Console.WriteLine(
            $"Stored {result.RowsStored:N0} rows (top {top} per query, by {options.Representation}) in {(DateTime.UtcNow - started).TotalSeconds:F1}s."
        );
        foreach (var s in result.Summary)
            Console.WriteLine(
                $"  {s.Split, -5} queries {s.Queries, 6:N0}  accuracy {Rate(s.Correct, s.Queries)}  "
                    + $"avg best {s.AverageBestSimilarity:F3}  ambiguous {Rate(s.Ambiguous, s.Queries)}"
            );
        return 0;
    }

    public static async Task<int> Evaluate(IServiceProvider services)
    {
        var results = await services.GetRequiredService<EvaluateHandler>().Handle();

        Console.WriteLine("Test split (held out):");
        foreach (var r in results)
            Console.WriteLine(
                $"  by {r.Representation, -8} top-1 {r.Top1Accuracy:P1}  top-3 {r.Top3Accuracy:P1}  "
                    + $"ambiguous {r.Ambiguous}/{r.Queries} (top-1 {r.AmbiguousTop1Accuracy:P1} vs {r.ConfidentTop1Accuracy:P1} when confident)"
            );
        return 0;
    }

    public static async Task<int> Classify(IServiceProvider services, CliOptions options)
    {
        var rawQuery = string.Join(' ', options.Positional);
        var top = options.Top ?? 3;
        var result = await services
            .GetRequiredService<ClassifyQueryHandler>()
            .Handle(rawQuery, top, options.Representation);

        Console.WriteLine($"Query:      {result.RawQuery}");
        Console.WriteLine($"Normalized: {result.Text}");
        Console.WriteLine(
            $"Embedding:  {result.EmbeddingSource}, by {options.Representation}{(result.IsAmbiguous ? ", AMBIGUOUS" : "")}"
        );
        foreach (var m in result.Candidates)
            Console.WriteLine($"  {m.Rank, 2}. {m.Similarity:F4}  {m.IntentName} ({m.IntentId})");
        return 0;
    }

    public static async Task<int> Label(IServiceProvider services, CliOptions options)
    {
        var result = await services
            .GetRequiredService<ConfirmLabelHandler>()
            .Handle(options.Positional[0], options.Positional[1]);

        Console.WriteLine($"Labelled \"{result.Text}\" as {result.IntentId}.");
        Console.WriteLine("Run `train` to fold it into the centroids.");
        return 0;
    }

    // A split only has zero queries on an empty/unseeded database; guarded so that reports an
    // n/a rate instead of NaN.
    private static string Rate(long numerator, long denominator) =>
        denominator == 0 ? "n/a" : ((double)numerator / denominator).ToString("P1");
}

internal sealed class ConsoleProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
