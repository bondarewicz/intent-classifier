namespace IntentClassifier.Infrastructure.Persistence;

using System.Globalization;
using Domain.Embeddings;
using Migrations;
using Pgvector;

internal static class VectorMapping
{
    public static Vector ToVector(this Embedding embedding)
    {
        EnsureDimensions(embedding);
        return new Vector(embedding.Values);
    }

    public static Embedding ToEmbedding(this Vector vector) => new(vector.Memory.Span);

    // pgvector text form "[0.1,0.2,...]", used to pass many vectors at once as text[]
    // (Dapper cannot bind an array of vectors directly).
    public static string ToVectorLiteral(this Embedding embedding)
    {
        EnsureDimensions(embedding);

        var span = embedding.Values.Span;
        var parts = new string[span.Length];
        for (var i = 0; i < span.Length; i++)
            parts[i] = span[i].ToString("R", CultureInfo.InvariantCulture);
        return "[" + string.Join(',', parts) + "]";
    }

    private static void EnsureDimensions(Embedding embedding)
    {
        if (embedding.Dimensions != DbNames.VectorDimensions)
            throw new InvalidOperationException(
                $"Embedding has {embedding.Dimensions} dimensions but the database column is "
                    + $"vector({DbNames.VectorDimensions}). Check OpenAi:Model - it must match the "
                    + "model the database was embedded with."
            );
    }
}
