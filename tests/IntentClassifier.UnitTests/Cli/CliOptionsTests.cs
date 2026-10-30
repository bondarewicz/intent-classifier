namespace IntentClassifier.UnitTests.Cli;

using IntentClassifier.Cli;

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
}
