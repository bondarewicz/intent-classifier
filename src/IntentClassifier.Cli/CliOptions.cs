namespace IntentClassifier.Cli;

using Domain.Intents;

internal sealed record CliOptions(
    string Command,
    IReadOnlyList<string> Positional,
    int? Top,
    int BatchSize,
    IntentRepresentation Representation,
    string TrainPath,
    string TestPath
)
{
    public const string Usage = """
        Usage:
          IntentClassifier.Cli migrate
          IntentClassifier.Cli seed [--train data/banking77/train.csv] [--test data/banking77/test.csv]
          IntentClassifier.Cli embed [--batch 1000]
          IntentClassifier.Cli train
          IntentClassifier.Cli classify-all [--top 5] [--by centroid|name]
          IntentClassifier.Cli evaluate
          IntentClassifier.Cli classify "<customer query>" [--top 3] [--by centroid|name]
          IntentClassifier.Cli label "<customer query>" <intent_id>

        Exit codes: 0 ok, 1 usage error (no/unknown command, bad flag), 2 runtime or
        configuration error (bad input, database unreachable, missing config).
        """;

    public static bool TryParse(string[] args, out CliOptions? options, out string? error)
    {
        options = null;

        if (args.Length == 0)
        {
            error = Usage;
            return false;
        }

        var positional = new List<string>();
        int? top = null;
        var batchSize = 1000;
        var representation = IntentRepresentation.Centroid;
        var trainPath = Path.Combine("data", "banking77", "train.csv");
        var testPath = Path.Combine("data", "banking77", "test.csv");

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--top":
                    if (
                        !TryTakeValue(args, ref i, out var topText)
                        || !int.TryParse(topText, out var parsedTop)
                    )
                    {
                        error = "--top requires an integer value.";
                        return false;
                    }
                    top = parsedTop;
                    break;
                case "--batch":
                    if (
                        !TryTakeValue(args, ref i, out var batchText)
                        || !int.TryParse(batchText, out batchSize)
                    )
                    {
                        error = "--batch requires an integer value.";
                        return false;
                    }
                    break;
                case "--by":
                    if (
                        !TryTakeValue(args, ref i, out var byText)
                        || !Enum.TryParse(byText, ignoreCase: true, out representation)
                        || !Enum.IsDefined(representation)
                    )
                    {
                        error = "--by must be 'centroid' or 'name'.";
                        return false;
                    }
                    break;
                case "--train":
                    if (!TryTakeValue(args, ref i, out trainPath))
                    {
                        error = "--train requires a path.";
                        return false;
                    }
                    break;
                case "--test":
                    if (!TryTakeValue(args, ref i, out testPath))
                    {
                        error = "--test requires a path.";
                        return false;
                    }
                    break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"Unknown option: {args[i]}";
                        return false;
                    }
                    positional.Add(args[i]);
                    break;
            }
        }

        error = null;
        options = new CliOptions(
            args[0],
            positional,
            top,
            batchSize,
            representation,
            trainPath,
            testPath
        );
        return true;
    }

    private static bool TryTakeValue(string[] args, ref int i, out string value)
    {
        if (i + 1 >= args.Length)
        {
            value = "";
            return false;
        }

        value = args[++i];
        return true;
    }
}
