using Atria.Domain.Common;
using Atria.Domain.Kyc;
using Atria.Domain.Kyc.Events;
using FluentAssertions;

namespace Atria.Domain.Tests.Kyc;

public sealed class KycProfileStateTests
{
    private static KycProfile NewSubmittedProfile()
    {
        var profile = KycProfile.Create(Guid.NewGuid());
        profile.Submit(KycProviderType.Didit, "session-1", "https://verify.didit.test/s/1",
            "0x" + new string('a', 40), "Jane Doe", "AB123456", "KZ");
        return profile;
    }

    [Fact]
    public void Create_StartsInPending_WithNoEvents()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var profile = KycProfile.Create(userId);

        // Assert
        profile.Status.Should().Be(KycStatus.Pending);
        profile.UserId.Should().Be(userId);
        profile.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Create_Submit_Approve_HappyPath_SetsApprovedAndRaisesEvents()
    {
        // Arrange
        var profile = KycProfile.Create(Guid.NewGuid());

        // Act
        profile.Submit(KycProviderType.Didit, "session-1", "https://verify.didit.test/s/1",
            "0x" + new string('b', 40), "Jane Doe", "AB123456", "KZ");
        profile.Approve();

        // Assert
        profile.Status.Should().Be(KycStatus.Approved);
        profile.Provider.Should().Be(KycProviderType.Didit);
        profile.ProviderSessionId.Should().Be("session-1");
        profile.DomainEvents.Should().ContainSingle(e => e is KycSubmittedEvent);
        profile.DomainEvents.Should().ContainSingle(e => e is KycApprovedEvent);
    }

    [Fact]
    public void Submit_RaisesKycSubmittedEventCarryingIds()
    {
        // Arrange
        var profile = KycProfile.Create(Guid.NewGuid());

        // Act
        profile.Submit(KycProviderType.Didit, "session-1", null, null, null, null, null);

        // Assert
        profile.Status.Should().Be(KycStatus.UnderReview);
        var submitted = profile.DomainEvents.OfType<KycSubmittedEvent>().Single();
        submitted.KycProfileId.Should().Be(profile.Id);
        submitted.UserId.Should().Be(profile.UserId);
    }

    [Fact]
    public void Reject_FromUnderReview_SetsRejectionReasonAndRejectedStatus()
    {
        // Arrange
        var profile = NewSubmittedProfile();

        // Act
        profile.Reject("Document blurry");

        // Assert
        profile.Status.Should().Be(KycStatus.Rejected);
        profile.RejectionReason.Should().Be("Document blurry");
        var rejected = profile.DomainEvents.OfType<KycRejectedEvent>().Single();
        rejected.Reason.Should().Be("Document blurry");
    }

    [Fact]
    public void Approve_OnPending_Throws()
    {
        // Arrange
        var profile = KycProfile.Create(Guid.NewGuid());

        // Act
        var act = () => profile.Approve();

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
        profile.Status.Should().Be(KycStatus.Pending);
    }

    [Fact]
    public void Reject_OnPending_Throws()
    {
        // Arrange
        var profile = KycProfile.Create(Guid.NewGuid());

        // Act
        var act = () => profile.Reject("nope");

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Submit_Twice_Throws()
    {
        // Arrange
        var profile = NewSubmittedProfile();

        // Act
        var act = () => profile.Submit(KycProviderType.Didit, "session-2", null, null, null, null, null);

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
        profile.Status.Should().Be(KycStatus.UnderReview);
    }

    [Fact]
    public void Approve_AfterApproved_Terminal_Throws()
    {
        // Arrange
        var profile = NewSubmittedProfile();
        profile.Approve();

        // Act
        var act = () => profile.Approve();

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
        profile.Status.Should().Be(KycStatus.Approved);
    }

    [Fact]
    public void Reject_AfterApproved_Terminal_Throws()
    {
        // Arrange
        var profile = NewSubmittedProfile();
        profile.Approve();

        // Act
        var act = () => profile.Reject("too late");

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Approve_AfterRejected_Terminal_Throws()
    {
        // Arrange
        var profile = NewSubmittedProfile();
        profile.Reject("bad docs");

        // Act
        var act = () => profile.Approve();

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
        profile.Status.Should().Be(KycStatus.Rejected);
    }
}
