namespace IntentClassifier.Application.Abstractions;

using Domain.Embeddings;

public interface IEmbeddingGenerator
{
    // Throws EmbeddingUnavailableException when the generator is not configured.
    Task<Embedding> Generate(string text, CancellationToken ct = default);

    // Embeddings in the same order as the input.
    Task<IReadOnlyList<Embedding>> GenerateBatch(
        IReadOnlyList<string> texts,
        CancellationToken ct = default
    );
}
