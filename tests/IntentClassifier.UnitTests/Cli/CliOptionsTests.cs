namespace IntentClassifier.UnitTests.Cli;

using IntentClassifier.Cli;
using IntentClassifier.Domain.Intents;

public sealed class CliOptionsTests
{
    [Fact]
    public void Valid_top_option_is_parsed()
    {
        var parsed = CliOptions.TryParse(
            ["classify-all", "--top", "5"],
            out var options,
            out var error
        );

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal(5, options!.Top);
    }

    [Fact]
    public void Unknown_flag_is_rejected_instead_of_becoming_positional()
    {
        var parsed = CliOptions.TryParse(
            ["classify", "x", "--tpo", "5"],
            out var options,
            out var error
        );

        Assert.False(parsed);
        Assert.Null(options);
        Assert.Equal("Unknown option: --tpo", error);
    }

    [Fact]
    public void Flag_missing_its_value_is_rejected()
    {
        var parsed = CliOptions.TryParse(["classify-all", "--top"], out var options, out var error);

        Assert.False(parsed);
        Assert.Null(options);
        Assert.Equal("--top requires an integer value.", error);
    }

    [Fact]
    public void Every_option_and_positional_argument_is_parsed()
    {
        var parsed = CliOptions.TryParse(
            [
                "seed",
                "extra",
                "--batch",
                "50",
                "--by",
                "Name",
                "--train",
                "a.csv",
                "--test",
                "b.csv",
            ],
            out var options,
            out var error
        );

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal("seed", options!.Command);
        Assert.Equal(["extra"], options.Positional);
        Assert.Equal(50, options.BatchSize);
        Assert.Equal(IntentRepresentation.Name, options.Representation);
        Assert.Equal("a.csv", options.TrainPath);
        Assert.Equal("b.csv", options.TestPath);
    }

    [Theory]
    [InlineData("nearest")]
    [InlineData("7")]
    public void Unknown_representation_is_rejected(string value)
    {
        var parsed = CliOptions.TryParse(["classify", "x", "--by", value], out _, out var error);

        Assert.False(parsed);
        Assert.Equal("--by must be 'centroid' or 'name'.", error);
    }
}
