using Atria.Domain.Common;
using Atria.Domain.Kyc;
using FluentAssertions;

namespace Atria.Domain.Tests.Kyc;

/// <summary>
/// Moving the allocation address.
/// <para>
/// The address is where shares and dividends land, so replacing one is deliberately not the same
/// operation as linking the first: <see cref="KycProfile.LinkWallet"/> is routine, this is not, and
/// the two must not share a path that could be reached by accident.
/// </para>
/// </summary>
public sealed class KycWalletChangeTests
{
    private const string First = "0x7F2C9A3F3B4E1D8B8D2A4F6E6B1c2D3e4F5a6B7c";
    private const string Second = "0x329c02528676DDef013B27AaD275d4c406927029";

    private static KycProfile Linked()
    {
        var profile = KycProfile.Create(Guid.NewGuid());
        profile.LinkWallet(First);
        return profile;
    }

    [Fact]
    public void ReplaceWallet_MovesTheAllocationAddress()
    {
        var profile = Linked();

        profile.ReplaceWallet(Second);

        profile.WalletAddress.Should().Be(Second);
    }

    [Fact]
    public void ReplaceWallet_AnnouncesTheNewAddress()
    {
        var profile = Linked();
        profile.ClearEvents();

        profile.ReplaceWallet(Second);

        // Every module that copied the old address learns about the new one; a replacement nobody
        // hears about is worse than one that never happened.
        profile.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<Domain.Kyc.Events.KycWalletLinkedEvent>()
            .Which.WalletAddress.Should().Be(Second);
    }

    [Fact]
    public void ReplaceWallet_RefusesWhenNothingIsLinkedYet()
    {
        var profile = KycProfile.Create(Guid.NewGuid());

        var act = () => profile.ReplaceWallet(Second);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReplaceWallet_RefusesTheAddressItAlreadyHas()
    {
        var profile = Linked();

        // Not a change: letting it through would raise an event and re-notify every module for nothing.
        var act = () => profile.ReplaceWallet(First);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReplaceWallet_TreatsCaseAsTheSameAddress()
    {
        var profile = Linked();

        var act = () => profile.ReplaceWallet(First.ToLowerInvariant());

        // EVM addresses are case-insensitive; the same address in another casing is still no change.
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReplaceWallet_RefusesAnEmptyAddress()
    {
        var profile = Linked();

        var act = () => profile.ReplaceWallet("   ");

        act.Should().Throw<DomainException>();
        profile.WalletAddress.Should().Be(First);
    }
}
