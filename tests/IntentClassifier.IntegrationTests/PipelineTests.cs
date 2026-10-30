namespace IntentClassifier.IntegrationTests;

using IntentClassifier.Application.Abstractions;
using IntentClassifier.Application.Classification;
using IntentClassifier.Application.Embeddings;
using IntentClassifier.Application.Evaluation;
using IntentClassifier.Application.Intents;
using IntentClassifier.Application.Queries;
using IntentClassifier.Domain.Intents;
using IntentClassifier.Domain.Queries;
using Microsoft.Extensions.DependencyInjection;

// Runs the real migrations, COPY import, Dapper repositories and pgvector SQL end to end, against
// the dataset PostgresFixture seeds, embeds and trains once in InitializeAsync. Each stage is its
// own Fact so a regression names the stage instead of hiding behind one long scenario.
public sealed class PipelineTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private T Get<T>()
        where T : notnull => db.Services!.GetRequiredService<T>();

    [Fact]
    public void Migrations_are_idempotent() => Get<IDatabaseMigrator>().MigrateUp();

    [Fact]
    public async Task Seed_is_idempotent()
    {
        // labels canonicalized, duplicate train text collapsed, the train/test overlap kept in both
        var seeded = await Get<IDatasetImporter>().ImportBanking77(db.TrainCsv, db.TestCsv);
        Assert.Equal(new DatasetImportResult(Intents: 4, TrainQueries: 10, TestQueries: 5), seeded);
    }

    [Fact]
    public async Task Embed_is_idempotent_once_everything_is_embedded()
    {
        var embedded = await Get<EmbedDatasetHandler>().Handle(batchSize: 4);
        Assert.Equal(new EmbedDatasetResult(0, 0), embedded);
    }

    [Fact]
    public async Task Train_recomputes_centroids_for_every_intent() =>
        Assert.Equal(4, await Get<TrainIntentsHandler>().Handle());

    [Fact]
    public async Task Known_query_reuses_its_stored_embedding_and_is_classified_as_its_labelled_intent()
    {
        var result = await Get<ClassifyQueryHandler>().Handle("My card has NOT arrived yet", 3);

        Assert.Equal(EmbeddingSource.Stored, result.EmbeddingSource);
        Assert.Equal("card_arrival", result.Candidates[0].IntentId.Value);
        Assert.Equal([1, 2, 3], result.Candidates.Select(m => m.Rank));
        Assert.True(result.Candidates[0].Similarity >= result.Candidates[1].Similarity);
    }

    [Fact]
    public async Task New_query_is_embedded_and_classified_as_the_intent_it_describes()
    {
        var result = await Get<ClassifyQueryHandler>().Handle("why is my refund not showing", 3);

        Assert.Equal(EmbeddingSource.Generated, result.EmbeddingSource);
        Assert.Equal("refund_not_showing_up", result.Candidates[0].IntentId.Value);
    }

    [Fact]
    public async Task Classifying_the_whole_dataset_stores_top_k_per_query_and_summarizes_by_split()
    {
        var classified = await Get<ClassifyAllHandler>().Handle(top: 3);

        Assert.Equal(15 * 3, classified.RowsStored);
        Assert.Equal(
            [DatasetSplit.Train, DatasetSplit.Test],
            classified.Summary.Select(s => s.Split)
        );
        Assert.Equal(10, classified.Summary[0].Queries);
        Assert.Equal(5, classified.Summary[1].Queries);
    }

    [Fact]
    public async Task Evaluate_reports_accuracy_for_both_representations()
    {
        var evaluation = await Get<EvaluateHandler>().Handle();

        Assert.Equal(
            [IntentRepresentation.Centroid, IntentRepresentation.Name],
            evaluation.Select(e => e.Representation)
        );
        Assert.All(evaluation, e => Assert.Equal(5, e.Queries));
        var centroid = evaluation[0];
        Assert.True(centroid.Top1Correct >= 4, $"centroid top-1 was {centroid.Top1Correct}/5");
        Assert.True(centroid.Top3Correct >= centroid.Top1Correct);
    }
}
