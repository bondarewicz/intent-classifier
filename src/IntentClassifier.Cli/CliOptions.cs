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
        var parsed = new CliOptions(
            Command: args[0],
            Positional: positional,
            Top: null,
            BatchSize: 1000,
            Representation: IntentRepresentation.Centroid,
            TrainPath: Path.Combine("data", "banking77", "train.csv"),
            TestPath: Path.Combine("data", "banking77", "test.csv")
        );

        for (var i = 1; i < args.Length; i++)
        {
            if (!TryApplyOption(args, ref i, ref parsed, positional, out error))
                return false;
        }

        error = null;
        options = parsed;
        return true;
    }

    private sealed record Flag(string Error, Func<string, CliOptions, CliOptions?> Apply);

    // Every flag takes the next argument as its value; Apply returns null when that value is invalid.
    private static readonly Dictionary<string, Flag> Flags = new()
    {
        ["--top"] = new(
            "--top requires an integer value.",
            (value, o) => int.TryParse(value, out var top) ? o with { Top = top } : null
        ),
        ["--batch"] = new(
            "--batch requires an integer value.",
            (value, o) => int.TryParse(value, out var batch) ? o with { BatchSize = batch } : null
        ),
        ["--by"] = new(
            "--by must be 'centroid' or 'name'.",
            (value, o) =>
                TryParseRepresentation(value, out var representation)
                    ? o with
                    {
                        Representation = representation,
                    }
                    : null
        ),
        ["--train"] = new("--train requires a path.", (value, o) => o with { TrainPath = value }),
        ["--test"] = new("--test requires a path.", (value, o) => o with { TestPath = value }),
    };

    // Applies args[i] to options, advancing i past a flag's value.
    private static bool TryApplyOption(
        string[] args,
        ref int i,
        ref CliOptions options,
        List<string> positional,
        out string? error
    )
    {
        var arg = args[i];
        if (Flags.TryGetValue(arg, out var flag))
            return TryApplyFlag(flag, args, ref i, ref options, out error);

        if (arg.StartsWith("--", StringComparison.Ordinal))
        {
            error = $"Unknown option: {arg}";
            return false;
        }

        positional.Add(arg);
        error = null;
        return true;
    }

    private static bool TryApplyFlag(
        Flag flag,
        string[] args,
        ref int i,
        ref CliOptions options,
        out string? error
    )
    {
        if (
            !TryTakeValue(args, ref i, out var value)
            || flag.Apply(value, options) is not { } applied
        )
        {
            error = flag.Error;
            return false;
        }

        options = applied;
        error = null;
        return true;
    }

    private static bool TryParseRepresentation(
        string value,
        out IntentRepresentation representation
    ) =>
        Enum.TryParse(value, ignoreCase: true, out representation)
        && Enum.IsDefined(representation);

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
