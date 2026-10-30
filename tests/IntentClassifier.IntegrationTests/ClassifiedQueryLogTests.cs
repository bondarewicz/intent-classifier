namespace IntentClassifier.IntegrationTests;

using Application.Classification;
using Application.Intents;
using Application.Queries;
using Dapper;
using Domain.Embeddings;
using Domain.Intents;
using Domain.Queries;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// A separate PostgresFixture instance (its own container) from PipelineTests, because labelling
// writes labelled_queries and must not change PipelineTests' exact row-count assertions.
public sealed class ClassifiedQueryLogTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Generated_query_is_logged_and_reused_without_a_second_generator_call()
    {
        var first = await Classify("i lost my card while traveling abroad");
        var second = await Classify("i lost my card while traveling abroad");

        Assert.Equal(EmbeddingSource.Generated, first.EmbeddingSource);
        Assert.Equal(EmbeddingSource.Stored, second.EmbeddingSource);
    }

    [Fact]
    public async Task Repeated_classification_increments_times_classified()
    {
        const string query = "where is my replacement card please";

        await Classify(query);
        await Classify(query);
        await Classify(query);

        Assert.Equal(3, await TimesClassified(query));
    }

    [Fact]
    public async Task Repeat_record_never_overwrites_the_original_embedding()
    {
        const int dims = HashingEmbeddingGenerator.Dimensions;
        var text = QueryText.Create("embedding stays put across repeats");
        var first = new Embedding(Enumerable.Repeat(0.1f, dims).ToArray());
        var second = new Embedding(Enumerable.Repeat(0.9f, dims).ToArray());
        var log = Get<IClassifiedQueryLog>();

        await log.Record(text, first, IntentRepresentation.Centroid, null, null, false);
        await log.Record(text, second, IntentRepresentation.Centroid, null, null, false);

        var stored = await log.FindEmbedding(text);
        Assert.Equal(first.Values.ToArray(), stored!.Values.ToArray());
    }

    [Fact]
    public async Task Classifying_the_same_query_twice_keeps_the_same_stored_embedding()
    {
        const string query = "the embedding should not drift on repeat classify";
        var log = Get<IClassifiedQueryLog>();

        await Classify(query);
        var afterFirst = await log.FindEmbedding(QueryText.Create(query));

        await Classify(query);
        var afterSecond = await log.FindEmbedding(QueryText.Create(query));

        Assert.Equal(afterFirst!.Values.ToArray(), afterSecond!.Values.ToArray());
    }

    [Fact]
    public async Task Label_rejects_a_query_that_was_never_classified()
    {
        await Assert.ThrowsAsync<InvalidQueryException>(() =>
            Label("never sent to classify", "card_arrival")
        );
    }

    [Fact]
    public async Task Label_rejects_an_intent_that_does_not_exist()
    {
        const string query = "a query for a bad intent test";
        await Classify(query);

        await Assert.ThrowsAsync<InvalidQueryException>(() => Label(query, "not_a_real_intent"));
    }

    [Fact]
    public async Task Label_rejects_a_query_present_in_the_test_split()
    {
        // Seeded in both splits (Banking77TestData's deliberate train/test overlap).
        const string query = "My card has not arrived yet";
        await Classify(query);

        await Assert.ThrowsAsync<InvalidQueryException>(() => Label(query, "card_arrival"));
    }

    [Fact]
    public async Task Label_rejects_a_query_already_labelled_in_the_dataset()
    {
        // Train-only (not in the test split).
        const string query = "I was charged twice for one payment";
        await Classify(query);

        await Assert.ThrowsAsync<InvalidQueryException>(() =>
            Label(query, "transaction_charged_twice")
        );
    }

    [Fact]
    public async Task Label_promotes_a_new_query_and_train_folds_it_in()
    {
        const string query = "my new card never showed up";
        var before = await TrainCount();

        await Classify(query);
        var result = await Label(query, "card_arrival");

        Assert.Equal("card_arrival", result.IntentId.Value);
        Assert.Equal(before + 1, await TrainCount());

        var intents = await Get<TrainIntentsHandler>().Handle();
        Assert.Equal(4, intents);
    }

    [Fact]
    public async Task Label_accepts_the_query_and_intent_in_any_casing_and_spacing()
    {
        const string query = "my replacement card still has not shown up";
        await Classify(query);

        await Label("  My REPLACEMENT card still has not   shown up ", "Card_Arrival");

        Assert.Equal("card_arrival", await TrainIntentId(query));
    }

    [Fact]
    public async Task Relabelling_updates_the_promoted_row_and_is_idempotent_for_the_same_intent()
    {
        const string query = "another brand new card question for relabelling";
        var before = await TrainCount();

        await Classify(query);
        await Label(query, "card_arrival");
        Assert.Equal("card_arrival", await TrainIntentId(query));

        await Label(query, "transaction_charged_twice");
        Assert.Equal("transaction_charged_twice", await TrainIntentId(query));
        Assert.Equal(before + 1, await TrainCount()); // updated in place, not a second row

        await Label(query, "transaction_charged_twice");
        Assert.Equal("transaction_charged_twice", await TrainIntentId(query));
        Assert.Equal(before + 1, await TrainCount());
    }

    private T Get<T>()
        where T : notnull => db.Services!.GetRequiredService<T>();

    // These tests never look at the candidates, so top is fixed to the API's 3.
    private Task<ClassifyQueryResult> Classify(string query) =>
        Get<ClassifyQueryHandler>().Handle(query, top: 3);

    private Task<ConfirmLabelResult> Label(string query, string intentId) =>
        Get<ConfirmLabelHandler>().Handle(query, intentId);

    private async Task<T> Query<T>(string sql, object? param = null)
    {
        await using var conn = await Get<NpgsqlDataSource>().OpenConnectionAsync();
        return await conn.QuerySingleAsync<T>(sql, param);
    }

    private Task<int> TimesClassified(string queryText) =>
        Query<int>(
            "SELECT times_classified FROM classified_queries WHERE query_text = @QueryText",
            new { QueryText = queryText }
        );

    private Task<int> TrainCount() =>
        Query<int>("SELECT count(*) FROM labelled_queries WHERE split = 'train'");

    private Task<string> TrainIntentId(string queryText) =>
        Query<string>(
            "SELECT intent_id FROM labelled_queries WHERE split = 'train' AND query_text = @QueryText",
            new { QueryText = queryText }
        );
}
