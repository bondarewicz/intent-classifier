namespace IntentClassifier.Application.Embeddings;

using Abstractions;
using Domain.Embeddings;
using Domain.Intents;
using Domain.Queries;

public sealed record EmbedDatasetProgress(string Stage, int Embedded, int Total);

public sealed record EmbedDatasetResult(int IntentNames, int Queries);

// Embeds intent names and labelled queries that have no embedding yet. Each batch is saved before
// the next one, so an interrupted run resumes where it stopped.
public sealed class EmbedDatasetHandler(
    IIntentRepository intents,
    ILabelledQueryRepository queries,
    IEmbeddingGenerator embeddingGenerator
)
{
    public async Task<EmbedDatasetResult> Handle(
        int batchSize,
        IProgress<EmbedDatasetProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var pendingIntents = await intents.ListWithoutNameEmbedding(ct);
        var embeddedIntents = await EmbedPending(
            pendingIntents,
            batchSize,
            "intents",
            textOf: i => i.Name,
            assign: (i, e) => i.AssignNameEmbedding(e),
            save: intents.SaveNameEmbeddings,
            progress,
            ct
        );

        var pendingQueries = await queries.ListWithoutEmbedding(ct);
        var embeddedQueries = await EmbedPending(
            pendingQueries,
            batchSize,
            "queries",
            textOf: q => q.Text.Value,
            assign: (q, e) => q.AssignEmbedding(e),
            save: queries.SaveEmbeddings,
            progress,
            ct
        );

        return new EmbedDatasetResult(embeddedIntents, embeddedQueries);
    }

    private async Task<int> EmbedPending<T>(
        IReadOnlyList<T> pending,
        int batchSize,
        string stage,
        Func<T, string> textOf,
        Action<T, Embedding> assign,
        Func<IReadOnlyCollection<T>, CancellationToken, Task> save,
        IProgress<EmbedDatasetProgress>? progress,
        CancellationToken ct
    )
    {
        var embedded = 0;
        foreach (var batch in pending.Chunk(batchSize))
        {
            var embeddings = await embeddingGenerator.GenerateBatch(
                batch.Select(textOf).ToList(),
                ct
            );
            for (var i = 0; i < batch.Length; i++)
                assign(batch[i], embeddings[i]);

            await save(batch, ct);
            embedded += batch.Length;
            progress?.Report(new EmbedDatasetProgress(stage, embedded, pending.Count));
        }

        return embedded;
    }
}
