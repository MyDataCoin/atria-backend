using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// Issue terms are a draft parameter until sales open, and a promise afterwards. Pinned both ways.
/// </summary>
public sealed class PropertyIssueTermsTests
{
    private static Property NewIssue() => Property.Create("Офис Г1", null, null, 1_000_000m, 1_000m, 1_000L, "KGS");

    [Fact]
    public void ReviseIssueTerms_ChangesTheTermsOfADraft()
    {
        var issue = NewIssue();

        issue.ReviseIssueTerms(totalValue: 2_000_000m, tokenPrice: 500m, totalTokens: 4_000L, minPurchaseTokens: 10L);

        issue.TotalValue.Should().Be(2_000_000m);
        issue.TokenPrice.Should().Be(500m);
        issue.TotalTokens.Should().Be(4_000L);
        issue.AvailableTokens.Should().Be(4_000L);
        issue.MinPurchaseTokens.Should().Be(10L);
    }

    [Fact]
    public void ReviseIssueTerms_IsAllowedWhileComingSoon()
    {
        var issue = NewIssue();
        issue.Announce();

        issue.ReviseIssueTerms(tokenPrice: 800m);

        issue.TokenPrice.Should().Be(800m);
    }

    [Fact]
    public void ReviseIssueTerms_IsRefusedOnceSalesOpen()
    {
        var issue = NewIssue();
        issue.Publish();

        var act = () => issue.ReviseIssueTerms(tokenPrice: 800m);

        act.Should().Throw<DomainException>();
        issue.TokenPrice.Should().Be(1_000m);
    }

    [Fact]
    public void ReviseIssueTerms_IsRefusedOnceAShareIsReserved()
    {
        var issue = NewIssue();
        issue.ReserveTokens(1);

        var act = () => issue.ReviseIssueTerms(totalTokens: 2_000L);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReviseIssueTerms_IsRefusedOnceTheContractIsDeployed()
    {
        var issue = NewIssue();
        issue.SetTokenContract("0xabc", "56", "0xdef");

        var act = () => issue.ReviseIssueTerms(totalTokens: 2_000L);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReviseIssueTerms_RejectsAMinimumLargerThanTheIssue()
    {
        var act = () => NewIssue().ReviseIssueTerms(totalTokens: 5L, minPurchaseTokens: 10L);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReviseIssueTerms_WithNothingToChange_IsANoOpEvenWhenOpen()
    {
        // The edit form PATCHes descriptive fields of open issues all the time; sending no terms
        // must not trip the guard.
        var issue = NewIssue();
        issue.Publish();

        var act = () => issue.ReviseIssueTerms();

        act.Should().NotThrow();
    }
}
