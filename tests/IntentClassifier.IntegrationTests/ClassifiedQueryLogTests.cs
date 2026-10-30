namespace IntentClassifier.IntegrationTests;

using Dapper;
using IntentClassifier.Application.Intents;
using IntentClassifier.Application.Queries;
using IntentClassifier.Domain.Embeddings;
using IntentClassifier.Domain.Intents;
using IntentClassifier.Domain.Queries;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// A separate PostgresFixture instance (its own container) from PipelineTests, because labelling
// writes labelled_queries and must not change PipelineTests' exact row-count assertions.
public sealed class ClassifiedQueryLogTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private T Get<T>()
        where T : notnull => db.Services!.GetRequiredService<T>();

    private async Task<int> TimesClassified(string queryText)
    {
        await using var conn = await Get<NpgsqlDataSource>().OpenConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            "SELECT times_classified FROM classified_queries WHERE query_text = @QueryText",
            new { QueryText = queryText }
        );
    }

    private async Task<int> TrainCount()
    {
        await using var conn = await Get<NpgsqlDataSource>().OpenConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            "SELECT count(*) FROM labelled_queries WHERE split = 'train'"
        );
    }

    private async Task<string> TrainIntentId(string queryText)
    {
        await using var conn = await Get<NpgsqlDataSource>().OpenConnectionAsync();
        return await conn.QuerySingleAsync<string>(
            "SELECT intent_id FROM labelled_queries WHERE split = 'train' AND query_text = @QueryText",
            new { QueryText = queryText }
        );
    }

    [Fact]
    public async Task Generated_query_is_logged_and_reused_without_a_second_generator_call()
    {
        var first = await Get<ClassifyQueryHandler>()
            .Handle("i lost my card while traveling abroad", 3);
        var second = await Get<ClassifyQueryHandler>()
            .Handle("i lost my card while traveling abroad", 3);

        Assert.Equal(EmbeddingSource.Generated, first.EmbeddingSource);
        Assert.Equal(EmbeddingSource.Stored, second.EmbeddingSource);
    }

    [Fact]
    public async Task Repeated_classification_increments_times_classified()
    {
        const string query = "where is my replacement card please";

        await Get<ClassifyQueryHandler>().Handle(query, 3);
        await Get<ClassifyQueryHandler>().Handle(query, 3);
        await Get<ClassifyQueryHandler>().Handle(query, 3);

        Assert.Equal(3, await TimesClassified(query));
    }

    [Fact]
    public async Task Repeat_record_never_overwrites_the_original_embedding()
    {
        var text = QueryText.Create("embedding stays put across repeats");
        var dims = HashingEmbeddingGenerator.Dimensions;
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

        await Get<ClassifyQueryHandler>().Handle(query, 3);
        var afterFirst = await log.FindEmbedding(QueryText.Create(query));

        await Get<ClassifyQueryHandler>().Handle(query, 3);
        var afterSecond = await log.FindEmbedding(QueryText.Create(query));

        Assert.Equal(afterFirst!.Values.ToArray(), afterSecond!.Values.ToArray());
    }

    [Fact]
    public async Task Label_rejects_a_query_that_was_never_classified()
    {
        await Assert.ThrowsAsync<InvalidQueryException>(() =>
            Get<ConfirmLabelHandler>().Handle("never sent to classify", "card_arrival")
        );
    }

    [Fact]
    public async Task Label_rejects_an_intent_that_does_not_exist()
    {
        const string query = "a query for a bad intent test";
        await Get<ClassifyQueryHandler>().Handle(query, 3);

        await Assert.ThrowsAsync<InvalidQueryException>(() =>
            Get<ConfirmLabelHandler>().Handle(query, "not_a_real_intent")
        );
    }

    [Fact]
    public async Task Label_rejects_a_query_present_in_the_test_split()
    {
        // Seeded in both splits (Banking77TestData's deliberate train/test overlap).
        const string query = "My card has not arrived yet";
        await Get<ClassifyQueryHandler>().Handle(query, 3);

        await Assert.ThrowsAsync<InvalidQueryException>(() =>
            Get<ConfirmLabelHandler>().Handle(query, "card_arrival")
        );
    }

    [Fact]
    public async Task Label_rejects_a_query_already_labelled_in_the_dataset()
    {
        // Train-only (not in the test split).
        const string query = "I was charged twice for one payment";
        await Get<ClassifyQueryHandler>().Handle(query, 3);

        await Assert.ThrowsAsync<InvalidQueryException>(() =>
            Get<ConfirmLabelHandler>().Handle(query, "transaction_charged_twice")
        );
    }

    [Fact]
    public async Task Label_promotes_a_new_query_and_train_folds_it_in()
    {
        const string query = "my new card never showed up";
        var before = await TrainCount();

        await Get<ClassifyQueryHandler>().Handle(query, 3);
        var result = await Get<ConfirmLabelHandler>().Handle(query, "card_arrival");

        Assert.Equal("card_arrival", result.IntentId.Value);
        Assert.Equal(before + 1, await TrainCount());

        var intents = await Get<TrainIntentsHandler>().Handle();
        Assert.Equal(4, intents);
    }

    [Fact]
    public async Task Label_accepts_the_query_and_intent_in_any_casing_and_spacing()
    {
        const string query = "my replacement card still has not shown up";
        await Get<ClassifyQueryHandler>().Handle(query, 3);

        await Get<ConfirmLabelHandler>()
            .Handle("  My REPLACEMENT card still has not   shown up ", "Card_Arrival");

        Assert.Equal("card_arrival", await TrainIntentId(query));
    }

    [Fact]
    public async Task Relabelling_updates_the_promoted_row_and_is_idempotent_for_the_same_intent()
    {
        const string query = "another brand new card question for relabelling";
        var before = await TrainCount();

        await Get<ClassifyQueryHandler>().Handle(query, 3);
        await Get<ConfirmLabelHandler>().Handle(query, "card_arrival");
        Assert.Equal("card_arrival", await TrainIntentId(query));

        await Get<ConfirmLabelHandler>().Handle(query, "transaction_charged_twice");
        Assert.Equal("transaction_charged_twice", await TrainIntentId(query));
        Assert.Equal(before + 1, await TrainCount()); // updated in place, not a second row

        await Get<ConfirmLabelHandler>().Handle(query, "transaction_charged_twice");
        Assert.Equal("transaction_charged_twice", await TrainIntentId(query));
        Assert.Equal(before + 1, await TrainCount());
    }
}
