namespace IntentClassifier.Infrastructure.Persistence;

using Domain.Queries;

internal static class DatasetSplitMapping
{
    public static string ToDb(this DatasetSplit split) =>
        split switch
        {
            DatasetSplit.Train => "train",
            DatasetSplit.Test => "test",
            _ => throw new ArgumentOutOfRangeException(nameof(split), split, null),
        };

    public static DatasetSplit ToDatasetSplit(this string split) =>
        split switch
        {
            "train" => DatasetSplit.Train,
            "test" => DatasetSplit.Test,
            _ => throw new ArgumentOutOfRangeException(nameof(split), split, null),
        };
}
