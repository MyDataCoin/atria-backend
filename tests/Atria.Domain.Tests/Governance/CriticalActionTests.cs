using Atria.Domain.Common;
using Atria.Domain.Governance;
using FluentAssertions;

namespace Atria.Domain.Tests.Governance;

/// <summary>
/// The two-person rule lives in the aggregate, not in a handler or a policy, so there is no path to
/// the action that skips it.
/// </summary>
public sealed class CriticalActionTests
{
    private static readonly DateTime Now = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Requester = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Approver = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Target = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static CriticalAction Pending() =>
        CriticalAction.Request(CriticalActionKind.InvestorBlock, Target, "подозрительная активность", Requester, Now);

    [Fact]
    public void A_request_starts_pending_and_open_for_the_approval_window()
    {
        var action = Pending();

        action.Status.Should().Be(CriticalActionStatus.Pending);
        action.RequestedByUserId.Should().Be(Requester);
        action.ExpiresAtUtc.Should().Be(Now.Add(CriticalAction.ApprovalWindow));
        action.DecidedByUserId.Should().BeNull();
    }

    [Fact]
    public void The_requester_cannot_approve_their_own_request()
    {
        var action = Pending();

        var approve = () => action.Approve(Requester, Now.AddMinutes(5));

        approve.Should().Throw<DomainException>()
            .WithMessage("*cannot be approved by the person who requested it*");
        action.Status.Should().Be(CriticalActionStatus.Pending);
    }

    [Fact]
    public void The_requester_cannot_reject_their_own_request_either()
    {
        var action = Pending();

        var reject = () => action.Reject(Requester, "передумал", Now.AddMinutes(5));

        reject.Should().Throw<DomainException>();
        action.Status.Should().Be(CriticalActionStatus.Pending);
    }

    [Fact]
    public void A_second_person_approves_and_the_decision_is_recorded()
    {
        var action = Pending();
        var decidedAt = Now.AddMinutes(30);

        action.Approve(Approver, decidedAt);

        action.Status.Should().Be(CriticalActionStatus.Approved);
        action.DecidedByUserId.Should().Be(Approver);
        action.DecidedAtUtc.Should().Be(decidedAt);
    }

    [Fact]
    public void An_already_decided_request_cannot_be_decided_again()
    {
        var action = Pending();
        action.Approve(Approver, Now.AddMinutes(1));

        var again = () => action.Approve(Approver, Now.AddMinutes(2));

        again.Should().Throw<DomainException>().WithMessage("*already been decided*");
    }

    [Fact]
    public void Declining_requires_a_reason()
    {
        var action = Pending();

        var reject = () => action.Reject(Approver, "   ", Now.AddMinutes(1));

        reject.Should().Throw<DomainException>().WithMessage("*reason is required*");
        action.Status.Should().Be(CriticalActionStatus.Pending);
    }

    [Fact]
    public void A_declined_request_keeps_the_reason_it_was_declined_for()
    {
        var action = Pending();

        action.Reject(Approver, "оснований недостаточно", Now.AddMinutes(1));

        action.Status.Should().Be(CriticalActionStatus.Rejected);
        action.DecisionNote.Should().Be("оснований недостаточно");
        action.DecidedByUserId.Should().Be(Approver);
    }

    [Fact]
    public void A_request_cannot_be_approved_after_its_window_closes()
    {
        var action = Pending();
        var tooLate = Now.Add(CriticalAction.ApprovalWindow);

        action.IsExpiredAt(tooLate).Should().BeTrue();
        var approve = () => action.Approve(Approver, tooLate);

        approve.Should().Throw<DomainException>().WithMessage("*window*");
    }

    [Fact]
    public void Only_the_requester_can_withdraw_their_request()
    {
        var action = Pending();

        var byOther = () => action.Withdraw(Approver, Now.AddMinutes(1));
        byOther.Should().Throw<DomainException>();

        action.Withdraw(Requester, Now.AddMinutes(1));
        action.Status.Should().Be(CriticalActionStatus.Withdrawn);
    }

    [Fact]
    public void A_withdrawn_request_can_no_longer_be_approved()
    {
        var action = Pending();
        action.Withdraw(Requester, Now.AddMinutes(1));

        var approve = () => action.Approve(Approver, Now.AddMinutes(2));

        approve.Should().Throw<DomainException>();
    }
}
