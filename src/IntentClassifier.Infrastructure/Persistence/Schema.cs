namespace IntentClassifier.Infrastructure.Persistence;

using Domain.Intents;

// Runtime names of the tables and view the repositories query. Migrations keep their own copy
// (DbNames) so an applied migration never changes when one of these is renamed.
internal static class Schema
{
    public const string Intents = "intents";
    public const string LabelledQueries = "labelled_queries";
    public const string ClassifiedQueries = "classified_queries";
    public const string QueryIntentCandidates = "query_intent_candidates";
    public const string QueryClassificationReport = "query_classification_report";

    public static string EmbeddingColumn(IntentRepresentation representation) =>
        representation switch
        {
            IntentRepresentation.Centroid => "centroid_embedding",
            IntentRepresentation.Name => "name_embedding",
            _ => throw new ArgumentOutOfRangeException(
                nameof(representation),
                representation,
                null
            ),
        };
}
