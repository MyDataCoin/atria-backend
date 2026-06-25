using Atria.Domain.Applications;
using Atria.Domain.Applications.Events;
using Atria.Domain.Common;
using Atria.Domain.Factories;
using Atria.Domain.Kyc;
using FluentAssertions;

namespace Atria.Domain.Tests.Applications;

public sealed class InvestorApplicationStateTests
{
    private static InvestorApplication NewDraft()
        => InvestorApplicationFactory.Create(
            Guid.NewGuid(), Guid.NewGuid(), 1000m, KycStatus.Approved);

    [Fact]
    public void Draft_Submit_Approve_HappyPath_SetsApprovedAndRaisesEvents()
    {
        // Arrange
        var application = NewDraft();

        // Act
        application.Submit();
        application.Approve();

        // Assert
        application.Status.Should().Be(ApplicationStatus.Approved);
        application.DomainEvents.Should().ContainSingle(e => e is ApplicationSubmittedEvent);
        application.DomainEvents.Should().ContainSingle(e => e is ApplicationApprovedEvent);
    }

    [Fact]
    public void Submit_FromDraft_RaisesSubmittedEventWithApplicationData()
    {
        // Arrange
        var application = NewDraft();

        // Act
        application.Submit();

        // Assert
        application.Status.Should().Be(ApplicationStatus.Submitted);
        var submitted = application.DomainEvents.OfType<ApplicationSubmittedEvent>().Single();
        submitted.ApplicationId.Should().Be(application.Id);
        submitted.InvestorId.Should().Be(application.InvestorId);
        submitted.PropertyId.Should().Be(application.PropertyId);
        submitted.Amount.Should().Be(application.Amount);
    }

    [Fact]
    public void Reject_FromSubmitted_SetsRejectionReasonAndRejected()
    {
        // Arrange
        var application = NewDraft();
        application.Submit();

        // Act
        application.Reject("AML hit");

        // Assert
        application.Status.Should().Be(ApplicationStatus.Rejected);
        application.RejectionReason.Should().Be("AML hit");
        application.DomainEvents.OfType<ApplicationRejectedEvent>().Single().Reason.Should().Be("AML hit");
    }

    [Fact]
    public void MoveToReview_ThenApprove_Works()
    {
        // Arrange
        var application = NewDraft();
        application.Submit();

        // Act
        application.MoveToReview();
        application.Approve();

        // Assert
        application.Status.Should().Be(ApplicationStatus.Approved);
        application.DomainEvents.Should().ContainSingle(e => e is ApplicationApprovedEvent);
    }

    [Fact]
    public void Approve_OnDraft_Throws()
    {
        // Arrange
        var application = NewDraft();

        // Act
        var act = () => application.Approve();

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
        application.Status.Should().Be(ApplicationStatus.Draft);
    }

    [Fact]
    public void Reject_OnDraft_Throws()
    {
        // Arrange
        var application = NewDraft();

        // Act
        var act = () => application.Reject("nope");

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Submit_OnSubmitted_Throws()
    {
        // Arrange
        var application = NewDraft();
        application.Submit();

        // Act
        var act = () => application.Submit();

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
        application.Status.Should().Be(ApplicationStatus.Submitted);
    }

    [Fact]
    public void TransitionsFromApproved_Terminal_Throw()
    {
        // Arrange
        var application = NewDraft();
        application.Submit();
        application.Approve();

        // Act / Assert
        application.Invoking(a => a.Submit()).Should().Throw<InvalidStateTransitionException>();
        application.Invoking(a => a.Approve()).Should().Throw<InvalidStateTransitionException>();
        application.Invoking(a => a.Reject("x")).Should().Throw<InvalidStateTransitionException>();
        application.Invoking(a => a.MoveToReview()).Should().Throw<InvalidStateTransitionException>();
        application.Status.Should().Be(ApplicationStatus.Approved);
    }

    [Fact]
    public void TransitionsFromRejected_Terminal_Throw()
    {
        // Arrange
        var application = NewDraft();
        application.Submit();
        application.Reject("bad");

        // Act / Assert
        application.Invoking(a => a.Submit()).Should().Throw<InvalidStateTransitionException>();
        application.Invoking(a => a.Approve()).Should().Throw<InvalidStateTransitionException>();
        application.Invoking(a => a.Reject("again")).Should().Throw<InvalidStateTransitionException>();
        application.Invoking(a => a.MoveToReview()).Should().Throw<InvalidStateTransitionException>();
        application.Status.Should().Be(ApplicationStatus.Rejected);
    }
}
