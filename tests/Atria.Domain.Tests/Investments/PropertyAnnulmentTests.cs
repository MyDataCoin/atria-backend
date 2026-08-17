using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// Suspension, annulment and invalidation — the three ways an issue stops (draft Decree, ch. 8,
/// ch. 11 and §73).
/// </summary>
public sealed class PropertyAnnulmentTests
{
    private static Property OpenIssue(decimal totalTokens = 1_000)
    {
        var p = Property.Create("Tower One", null, null, 1_000_000m, 100m, totalTokens, "KGS");
        p.Publish();
        return p;
    }

    // ── Annulment of unplaced capacity ───────────────────────────────────────

    [Fact]
    public void Annulling_unplaced_tokens_shrinks_the_issue_itself()
    {
        var property = OpenIssue();

        property.AnnulUnplacedTokens(300);

        property.TotalTokens.Should().Be(700);
        property.AvailableTokens.Should().Be(700);
    }

    /// <summary>Shares in an investor's hands are not the issuer's to cancel this way.</summary>
    [Fact]
    public void Only_capacity_that_is_still_unplaced_can_be_annulled()
    {
        var property = OpenIssue();
        property.ReserveTokens(400); // 600 left unplaced

        var act = () => property.AnnulUnplacedTokens(700);

        act.Should().Throw<DomainException>().WithMessage("*more tokens than remain unplaced*");
        property.TotalTokens.Should().Be(1_000);
    }

    [Fact]
    public void Annulling_the_remaining_capacity_leaves_only_what_was_placed()
    {
        var property = OpenIssue();
        property.ReserveTokens(250);

        property.AnnulUnplacedTokens(750);

        property.TotalTokens.Should().Be(250);
        property.AvailableTokens.Should().Be(0);
    }

    [Fact]
    public void A_non_positive_annulment_is_refused()
    {
        var property = OpenIssue();

        var act = () => property.AnnulUnplacedTokens(0);

        act.Should().Throw<DomainException>();
    }

    // ── Invalidation ─────────────────────────────────────────────────────────

    [Fact]
    public void Invalidating_an_issue_stops_its_sales_and_is_terminal()
    {
        var property = OpenIssue();

        property.Invalidate();

        property.Status.Should().Be(PropertyStatus.Invalidated);
        property.SalesPaused.Should().BeTrue();
    }

    [Fact]
    public void An_invalidated_issue_cannot_be_invalidated_again()
    {
        var property = OpenIssue();
        property.Invalidate();

        var act = () => property.Invalidate();

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void An_invalidated_issue_can_never_be_published_announced_or_completed_again()
    {
        var property = OpenIssue();
        property.Invalidate();

        ((Action)(() => property.Publish())).Should().Throw<InvalidStateTransitionException>();
        ((Action)(() => property.Announce())).Should().Throw<InvalidStateTransitionException>();
        ((Action)(() => property.Complete())).Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void An_issue_can_be_invalidated_from_any_live_state()
    {
        var draft = Property.Create("Draft", null, null, 1_000m, 10m, 100, "KGS");
        draft.Invalidate();
        draft.Status.Should().Be(PropertyStatus.Invalidated);

        var completed = OpenIssue();
        completed.Complete();
        completed.Invalidate();
        completed.Status.Should().Be(PropertyStatus.Invalidated);
    }

    // ── Suspension ───────────────────────────────────────────────────────────

    [Fact]
    public void Suspension_is_orthogonal_to_the_lifecycle_status()
    {
        var property = OpenIssue();

        property.PauseSales();

        property.SalesPaused.Should().BeTrue();
        property.Status.Should().Be(PropertyStatus.Open, "a suspension does not unpublish the issue");

        property.ResumeSales();
        property.SalesPaused.Should().BeFalse();
    }
}
