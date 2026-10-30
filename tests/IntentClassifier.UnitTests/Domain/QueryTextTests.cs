namespace IntentClassifier.UnitTests.Domain;

using IntentClassifier.Domain.Queries;

public sealed class QueryTextTests
{
    [Theory]
    [InlineData("How do I locate my card?", "how do i locate my card?")]
    [InlineData(
        "  I was CHARGED twice\tfor the\n same coffee  ",
        "i was charged twice for the same coffee"
    )]
    [InlineData("Where's my £ refund?!", "where's my £ refund?!")]
    public void Query_text_is_trimmed_lowercased_and_whitespace_collapsed_but_keeps_punctuation(
        string raw,
        string expected
    ) => Assert.Equal(expected, QueryText.Create(raw).Value);

    [Fact]
    public void Normalizing_an_already_normalized_query_changes_nothing()
    {
        var once = QueryText.Create("  Why is my TOP-UP pending? ");
        Assert.Equal(once, QueryText.Create(once.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n ")]
    public void Empty_query_is_rejected(string? raw) =>
        Assert.Throws<InvalidQueryException>(() => QueryText.Create(raw));

    [Fact]
    public void Query_over_the_maximum_length_is_rejected() =>
        Assert.Throws<InvalidQueryException>(() =>
            QueryText.Create(new string('a', QueryText.MaxLength + 1))
        );
}
