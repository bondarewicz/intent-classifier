namespace IntentClassifier.UnitTests.Domain;

using IntentClassifier.Domain.Intents;

public sealed class IntentIdTests
{
    [Theory]
    [InlineData("card_arrival", "card_arrival")]
    [InlineData("Refund_not_showing_up", "refund_not_showing_up")] // spelled like this in Banking77
    [InlineData("reverted_card_payment?", "reverted_card_payment")] // spelled like this in Banking77
    [InlineData(" top_up_failed ", "top_up_failed")]
    public void Intent_labels_are_canonicalized_to_lowercase_ids(string label, string expected) =>
        Assert.Equal(expected, IntentId.Create(label).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("???")]
    [InlineData("_leading")]
    [InlineData("double__underscore")]
    public void Malformed_intent_label_is_rejected(string? label) =>
        Assert.Throws<ArgumentException>(() => IntentId.Create(label));

    [Fact]
    public void Display_name_is_readable() =>
        Assert.Equal("Card arrival", IntentId.Create("card_arrival").ToDisplayName());

    [Fact]
    public void Ids_differing_only_in_spelling_are_equal() =>
        Assert.Equal(
            IntentId.Create("Refund_not_showing_up"),
            IntentId.Create("refund_not_showing_up")
        );
}
