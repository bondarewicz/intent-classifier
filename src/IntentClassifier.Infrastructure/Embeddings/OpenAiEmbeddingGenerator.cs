namespace IntentClassifier.Infrastructure.Embeddings;

using IntentClassifier.Application.Abstractions;
using IntentClassifier.Domain.Embeddings;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;

internal sealed class OpenAiEmbeddingGenerator(IOptions<OpenAiOptions> options)
    : IEmbeddingGenerator
{
    private const int MaxInputsPerRequest = 2048;

    private readonly Lazy<EmbeddingClient?> client = new(() =>
        string.IsNullOrEmpty(options.Value.ApiKey)
            ? null
            : new EmbeddingClient(options.Value.Model, options.Value.ApiKey)
    );

    public async Task<Embedding> Generate(string text, CancellationToken ct = default)
    {
        var response = await Client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return new Embedding(response.Value.ToFloats().Span);
    }

    public async Task<IReadOnlyList<Embedding>> GenerateBatch(
        IReadOnlyList<string> texts,
        CancellationToken ct = default
    )
    {
        var embeddings = new List<Embedding>(texts.Count);
        foreach (var chunk in texts.Chunk(MaxInputsPerRequest))
        {
            var response = await Client.GenerateEmbeddingsAsync(chunk, cancellationToken: ct);
            if (response.Value.Count != chunk.Length)
                throw new InvalidOperationException(
                    $"OpenAI returned {response.Value.Count} embeddings for a batch of {chunk.Length} texts."
                );

            var ordered = response.Value.OrderBy(e => e.Index).ToList();
            for (var i = 0; i < ordered.Count; i++)
                if (ordered[i].Index != i)
                    throw new InvalidOperationException(
                        $"OpenAI returned embeddings with unexpected Index values for a batch of {chunk.Length} texts."
                    );

            embeddings.AddRange(ordered.Select(e => new Embedding(e.ToFloats().Span)));
        }

        return embeddings;
    }

    private EmbeddingClient Client => client.Value ?? throw new EmbeddingUnavailableException();
}
