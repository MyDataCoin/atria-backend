using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// The encoding an issue's id travels into its token contract under. It is written once, immutably,
/// at deployment and compared against on every binding — so the two directions have to agree exactly.
/// </summary>
public sealed class PropertyChainIdTests
{
    [Fact]
    public void The_word_is_the_guid_left_aligned_and_zero_padded()
    {
        var id = Guid.Parse("3f8d9001-2b4c-4d6e-8a10-c0ffee001234");

        PropertyChainId.From(id).Should()
            .Be("0x3f8d90012b4c4d6e8a10c0ffee001234" + new string('0', 32));
    }

    [Fact]
    public void A_word_round_trips_back_to_the_issue_it_names()
    {
        var id = Guid.NewGuid();

        PropertyChainId.TryParse(PropertyChainId.From(id), out var parsed).Should().BeTrue();
        parsed.Should().Be(id);
        PropertyChainId.Matches(id, PropertyChainId.From(id)).Should().BeTrue();
    }

    /// <summary>The placeholder identifies nothing, so it must not read as any issue's id.</summary>
    [Fact]
    public void The_zero_word_names_no_issue()
    {
        PropertyChainId.TryParse(PropertyChainId.Unset, out _).Should().BeFalse();
        PropertyChainId.Matches(Guid.NewGuid(), PropertyChainId.Unset).Should().BeFalse();
    }

    /// <summary>
    /// Taking the first half of an arbitrary word would invent an id out of an unrelated value, so
    /// the padding is checked rather than assumed.
    /// </summary>
    [Fact]
    public void A_word_that_is_not_zero_padded_is_not_an_issue_id()
    {
        var id = Guid.NewGuid();
        var corrupted = PropertyChainId.From(id)[..^1] + "1";

        PropertyChainId.TryParse(corrupted, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x1234")]
    [InlineData("not a word")]
    public void Malformed_input_is_rejected(string value)
        => PropertyChainId.TryParse(value, out _).Should().BeFalse();
}
