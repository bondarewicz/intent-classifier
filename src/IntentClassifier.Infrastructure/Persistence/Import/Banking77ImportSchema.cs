namespace IntentClassifier.Infrastructure.Persistence.Import;

using Intents;
using LabelledQueries;

// Staging for the Banking77 CSVs (text,category): COPY loads them, then the domain types
// normalize and validate each row before insert.
internal static class Banking77ImportSchema
{
    public const string Stage = "banking77_stage";

    public static class Sql
    {
        public static readonly string CreateStage = $"""
            CREATE TEMP TABLE {Stage} (query_text text, category text, split text) ON COMMIT DROP
            """;

        public static readonly string CopyIntoStage =
            $"COPY {Stage} (query_text, category) FROM STDIN (FORMAT csv, HEADER true)";

        // Tags whichever rows were COPYed most recently; CopyIntoStage must mark immediately
        // after each COPY or every row would get the split marked last.
        public static readonly string MarkSplit =
            $"UPDATE {Stage} SET split = @Split WHERE split IS NULL";

        public static readonly string SelectStaged =
            $"SELECT query_text, category, split FROM {Stage}";

        public static readonly string AnalyzeImported =
            $"ANALYZE {IntentsSchema.Table}; ANALYZE {LabelledQueriesSchema.Table};";
    }

    internal sealed class StagedRow
    {
        public string QueryText { get; set; } = "";
        public string Category { get; set; } = "";
        public string Split { get; set; } = "";
    }
}
