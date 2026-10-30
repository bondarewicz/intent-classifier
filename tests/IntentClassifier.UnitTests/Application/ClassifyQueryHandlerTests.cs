namespace IntentClassifier.UnitTests.Application;

using IntentClassifier.Application.Abstractions;
using IntentClassifier.Application.Queries;
using IntentClassifier.Domain.Embeddings;
using IntentClassifier.Domain.Intents;
using IntentClassifier.Domain.Queries;

public sealed class ClassifyQueryHandlerTests
{
    private static LabelledQuery Known(string text, Embedding? embedding) =>
        new(
            Guid.NewGuid(),
            QueryText.Create(text),
            DatasetSplit.Test,
            IntentId.Create("card_arrival"),
            embedding
        );

    [Fact]
    public async Task Known_query_reuses_the_stored_embedding_without_generating_one()
    {
        var stored = new Embedding([0.1f, 0.2f]);
        var intents = new FakeIntentRepository();
        var generator = new FakeEmbeddingGenerator();
        var handler = new ClassifyQueryHandler(
            new InMemoryLabelledQueryRepository(Known("how do i locate my card?", stored)),
            new InMemoryClassifiedQueryLog(),
            intents,
            generator
        );

        var result = await handler.Handle("  How do I locate my CARD? ", 3);

        Assert.Equal(EmbeddingSource.Stored, result.EmbeddingSource);
        Assert.Equal("how do i locate my card?", result.Text.Value);
        Assert.Same(stored, intents.LastQuery);
        Assert.Empty(generator.Requested);
    }

    [Fact]
    public async Task Logged_query_reuses_its_embedding_without_generating_one()
    {
        var logged = new Embedding([0.3f, 0.4f]);
        var log = new InMemoryClassifiedQueryLog();
        log.Seed(QueryText.Create("where is my refund"), logged);
        var intents = new FakeIntentRepository();
        var generator = new FakeEmbeddingGenerator();
        var handler = new ClassifyQueryHandler(
            new InMemoryLabelledQueryRepository(),
            log,
            intents,
            generator
        );

        var result = await handler.Handle("where is my refund", 3);

        Assert.Equal(EmbeddingSource.Stored, result.EmbeddingSource);
        Assert.Same(logged, intents.LastQuery);
        Assert.Empty(generator.Requested);
    }

    [Fact]
    public async Task Labelled_embedding_is_preferred_over_the_logged_one()
    {
        var labelled = new Embedding([0.1f, 0.2f]);
        var logged = new Embedding([0.3f, 0.4f]);
        var log = new InMemoryClassifiedQueryLog();
        log.Seed(QueryText.Create("same text"), logged);
        var intents = new FakeIntentRepository();
        var handler = new ClassifyQueryHandler(
            new InMemoryLabelledQueryRepository(Known("same text", labelled)),
            log,
            intents,
            new FakeEmbeddingGenerator()
        );

        await handler.Handle("same text", 3);

        Assert.Same(labelled, intents.LastQuery);
    }

    [Fact]
    public async Task Unknown_query_is_embedded_from_its_normalized_text()
    {
        var intents = new FakeIntentRepository();
        var generator = new FakeEmbeddingGenerator();
        var handler = new ClassifyQueryHandler(
            new InMemoryLabelledQueryRepository(),
            new InMemoryClassifiedQueryLog(),
            intents,
            generator
        );

        var result = await handler.Handle("Charged TWICE for coffee", 3);

        Assert.Equal(EmbeddingSource.Generated, result.EmbeddingSource);
        Assert.Equal(["charged twice for coffee"], generator.Requested);
        Assert.Equal([1, 2, 3], result.Candidates.Select(m => m.Rank));
    }

    [Fact]
    public async Task Repeat_classification_of_a_generated_query_reuses_the_log_and_skips_the_generator()
    {
        var intents = new FakeIntentRepository();
        var generator = new FakeEmbeddingGenerator();
        var log = new InMemoryClassifiedQueryLog();
        var handler = new ClassifyQueryHandler(
            new InMemoryLabelledQueryRepository(),
            log,
            intents,
            generator
        );

        var first = await handler.Handle("a brand new question", 3);
        var second = await handler.Handle("a brand new question", 3);

        Assert.Equal(EmbeddingSource.Generated, first.EmbeddingSource);
        Assert.Equal(EmbeddingSource.Stored, second.EmbeddingSource);
        Assert.Equal(["a brand new question"], generator.Requested);
    }

    [Fact]
    public async Task Every_successful_classification_is_recorded_to_the_log()
    {
        var log = new InMemoryClassifiedQueryLog();
        var handler = new ClassifyQueryHandler(
            new InMemoryLabelledQueryRepository(),
            log,
            new FakeIntentRepository(),
            new FakeEmbeddingGenerator()
        );

        await handler.Handle("hello", 3);

        Assert.Equal(["hello"], log.RecordedCalls.Select(r => r.Text.Value));
        Assert.Equal(IntentId.Create("intent_1"), log.RecordedCalls[0].BestIntentId);
    }

    [Fact]
    public async Task Reusing_a_labelled_embedding_is_still_recorded_to_the_log()
    {
        var log = new InMemoryClassifiedQueryLog();
        var handler = new ClassifyQueryHandler(
            new InMemoryLabelledQueryRepository(
                Known("how do i locate my card?", new Embedding([0.1f, 0.2f]))
            ),
            log,
            new FakeIntentRepository(),
            new FakeEmbeddingGenerator()
        );

        var result = await handler.Handle("how do i locate my card?", 3);

        Assert.Equal(EmbeddingSource.Stored, result.EmbeddingSource);
        Assert.Single(log.RecordedCalls);
    }

    [Fact]
    public async Task Recording_the_same_text_twice_never_overwrites_its_first_embedding()
    {
        var log = new InMemoryClassifiedQueryLog();
        var first = new Embedding([0.1f, 0.2f]);
        var second = new Embedding([0.9f, 0.9f]);

        await log.Record(
            QueryText.Create("hello"),
            first,
            IntentRepresentation.Centroid,
            null,
            null,
            false
        );
        await log.Record(
            QueryText.Create("hello"),
            second,
            IntentRepresentation.Centroid,
            null,
            null,
            false
        );

        Assert.Same(first, await log.FindEmbedding(QueryText.Create("hello")));
    }

    [Fact]
    public async Task Uses_centroids_unless_asked_otherwise()
    {
        var intents = new FakeIntentRepository();
        var handler = new ClassifyQueryHandler(
            new InMemoryLabelledQueryRepository(),
            new InMemoryClassifiedQueryLog(),
            intents,
            new FakeEmbeddingGenerator()
        );

        await handler.Handle("hello", 3);
        Assert.Equal(IntentRepresentation.Centroid, intents.LastRepresentation);

        await handler.Handle("hello", 3, IntentRepresentation.Name);
        Assert.Equal(IntentRepresentation.Name, intents.LastRepresentation);
    }

    [Fact]
    public async Task Flags_ambiguity_even_when_only_the_best_candidate_is_requested()
    {
        var intents = new FakeIntentRepository { Similarities = [0.80, 0.79, 0.5] };
        var handler = new ClassifyQueryHandler(
            new InMemoryLabelledQueryRepository(),
            new InMemoryClassifiedQueryLog(),
            intents,
            new FakeEmbeddingGenerator()
        );

        var result = await handler.Handle("refund", top: 1);

        Assert.True(result.IsAmbiguous);
        Assert.Single(result.Candidates);
        Assert.Equal(2, intents.LastTop);
    }

    [Fact]
    public async Task Zero_candidates_are_recorded_with_no_best_and_not_ambiguous()
    {
        var log = new InMemoryClassifiedQueryLog();
        var handler = new ClassifyQueryHandler(
            new InMemoryLabelledQueryRepository(),
            log,
            new FakeIntentRepository { Similarities = [] },
            new FakeEmbeddingGenerator()
        );

        var result = await handler.Handle("refund", top: 3);

        Assert.Empty(result.Candidates);
        Assert.False(result.IsAmbiguous);
        var recorded = log.RecordedCalls[0];
        Assert.Null(recorded.BestIntentId);
        Assert.Null(recorded.BestSimilarity);
        Assert.False(recorded.Ambiguous);
    }

    [Fact]
    public async Task Invalid_query_is_rejected_before_any_lookup_and_never_logged()
    {
        var generator = new FakeEmbeddingGenerator();
        var log = new InMemoryClassifiedQueryLog();
        var handler = new ClassifyQueryHandler(
            new InMemoryLabelledQueryRepository(),
            log,
            new FakeIntentRepository(),
            generator
        );

        await Assert.ThrowsAsync<InvalidQueryException>(() => handler.Handle("   ", 3));
        Assert.Empty(generator.Requested);
        Assert.Empty(log.RecordedCalls);
    }

    [Fact]
    public async Task Unknown_query_without_generator_surfaces_unavailable()
    {
        var handler = new ClassifyQueryHandler(
            new InMemoryLabelledQueryRepository(),
            new InMemoryClassifiedQueryLog(),
            new FakeIntentRepository(),
            new FakeEmbeddingGenerator(available: false)
        );

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() =>
            handler.Handle("brand new question", 3)
        );
    }
}
