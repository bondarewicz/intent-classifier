namespace IntentClassifier.Infrastructure.Persistence.Migrations;

// Names used by migrations. Migrations keep their own constants (not Persistence/Schema.cs) so an
// applied migration never changes when a row property is renamed later.
internal static class DbNames
{
    // The embedding model's output size (text-embedding-3-small). Changing it needs a migration
    // and a full re-embed; see VectorMapping's dimension guard for the runtime side of this.
    public const int VectorDimensions = 1536;

    public static readonly string VectorType = $"vector({VectorDimensions})";

    public static class Intents
    {
        public const string Table = "intents";
        public const string Id = "intent_id";
    }

    public static class LabelledQueries
    {
        public const string Table = "labelled_queries";
        public const string Id = "query_id";
    }

    public static class QueryIntentMatches
    {
        public const string Table = "query_intent_matches";
    }

    public static class ClassifiedQueries
    {
        public const string Table = "classified_queries";
        public const string Id = "classified_query_id";
    }
}
