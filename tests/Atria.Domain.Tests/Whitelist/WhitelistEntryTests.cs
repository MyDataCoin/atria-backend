using Atria.Domain.Common;
using Atria.Domain.Whitelist;
using FluentAssertions;

namespace Atria.Domain.Tests.Whitelist;

public sealed class WhitelistEntryTests
{
    private const string Wallet = "0x1111111111111111111111111111111111111111";
    private const string OtherWallet = "0x2222222222222222222222222222222222222222";

    private static WhitelistEntry NewEntry(string? wallet = Wallet)
        => WhitelistEntry.Queue(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tokenCount: 10, wallet, DateTime.UtcNow);

    [Fact]
    public void Queue_ProducesPendingRequest()
    {
        var investmentId = Guid.NewGuid();
        var investorId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var requestedAt = DateTime.UtcNow;

        var entry = WhitelistEntry.Queue(investmentId, investorId, propertyId, 125, Wallet, requestedAt);

        entry.Status.Should().Be(WhitelistStatus.Pending);
        entry.InvestmentId.Should().Be(investmentId);
        entry.InvestorId.Should().Be(investorId);
        entry.PropertyId.Should().Be(propertyId);
        entry.TokenCount.Should().Be(125);
        entry.WalletAddress.Should().Be(Wallet);
        entry.RequestedAtUtc.Should().Be(requestedAt);
        entry.MintListId.Should().BeNull();
    }

    [Fact]
    public void Queue_WithoutWallet_IsAllowedButNotMintable()
    {
        var entry = NewEntry(wallet: null);

        entry.WalletAddress.Should().BeNull();
        entry.MarkReady(DateTime.UtcNow);
        entry.IsMintable.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Queue_WhenTokenCountNotPositive_Throws(long tokenCount)
    {
        var act = () => WhitelistEntry.Queue(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tokenCount, Wallet, DateTime.UtcNow);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Queue_WhenWalletMalformed_Throws()
    {
        var act = () => WhitelistEntry.Queue(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "not-an-address", DateTime.UtcNow);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkReady_MakesRequestMintable()
    {
        var entry = NewEntry();
        var approvedAt = DateTime.UtcNow;

        entry.MarkReady(approvedAt);

        entry.Status.Should().Be(WhitelistStatus.Ready);
        entry.ApprovedAtUtc.Should().Be(approvedAt);
        entry.IsMintable.Should().BeTrue();
    }

    [Fact]
    public void MarkReady_WhenNotPending_Throws()
    {
        var entry = NewEntry();
        entry.MarkReady(DateTime.UtcNow);

        var act = () => entry.MarkReady(DateTime.UtcNow);

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void IncludeIn_MovesRequestOutOfTheMintablePool()
    {
        var entry = NewEntry();
        entry.MarkReady(DateTime.UtcNow);
        var mintListId = Guid.NewGuid();

        entry.IncludeIn(mintListId);

        entry.Status.Should().Be(WhitelistStatus.Batched);
        entry.MintListId.Should().Be(mintListId);
        entry.IsMintable.Should().BeFalse();
    }

    [Fact]
    public void IncludeIn_WhenStillPending_Throws()
    {
        var entry = NewEntry();

        var act = () => entry.IncludeIn(Guid.NewGuid());

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void IncludeIn_WhenNoWallet_Throws()
    {
        var entry = NewEntry(wallet: null);
        entry.MarkReady(DateTime.UtcNow);

        var act = () => entry.IncludeIn(Guid.NewGuid());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReleaseFromMintList_ReturnsRequestToTheMintablePool()
    {
        var entry = NewEntry();
        entry.MarkReady(DateTime.UtcNow);
        entry.IncludeIn(Guid.NewGuid());

        entry.ReleaseFromMintList();

        entry.Status.Should().Be(WhitelistStatus.Ready);
        entry.MintListId.Should().BeNull();
        entry.IsMintable.Should().BeTrue();
    }

    [Fact]
    public void MarkMinted_RequiresTheRequestToHaveBeenBatched()
    {
        var entry = NewEntry();
        entry.MarkReady(DateTime.UtcNow);

        var act = () => entry.MarkMinted(DateTime.UtcNow);

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void MarkMinted_RecordsWhenTheSharesCameIntoExistence()
    {
        var entry = NewEntry();
        entry.MarkReady(DateTime.UtcNow);
        entry.IncludeIn(Guid.NewGuid());
        var mintedAt = DateTime.UtcNow;

        entry.MarkMinted(mintedAt);

        entry.Status.Should().Be(WhitelistStatus.Minted);
        entry.MintedAtUtc.Should().Be(mintedAt);
    }

    [Fact]
    public void SetWallet_WhenAlreadyBatched_Throws()
    {
        var entry = NewEntry();
        entry.MarkReady(DateTime.UtcNow);
        entry.IncludeIn(Guid.NewGuid());

        var act = () => entry.SetWallet(OtherWallet);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SetWallet_FillsInAnAddressTheRequestWasQueuedWithout()
    {
        var entry = NewEntry(wallet: null);

        entry.SetWallet(Wallet);

        entry.WalletAddress.Should().Be(Wallet);
    }

    [Fact]
    public void Exclude_DropsTheRequestAndKeepsTheReason()
    {
        var entry = NewEntry();

        entry.Exclude("Заявка отклонена оператором");

        entry.Status.Should().Be(WhitelistStatus.Excluded);
        entry.ExclusionReason.Should().Be("Заявка отклонена оператором");
        entry.MintListId.Should().BeNull();
    }

    [Fact]
    public void Exclude_IsIdempotent()
    {
        var entry = NewEntry();
        entry.Exclude("Первая причина");

        entry.Exclude("Вторая причина");

        entry.ExclusionReason.Should().Be("Первая причина");
    }

    [Fact]
    public void Exclude_WhenAlreadyMinted_Throws()
    {
        var entry = NewEntry();
        entry.MarkReady(DateTime.UtcNow);
        entry.IncludeIn(Guid.NewGuid());
        entry.MarkMinted(DateTime.UtcNow);

        var act = () => entry.Exclude("Инвестор отказался");

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Exclude_WithoutReason_Throws()
    {
        var entry = NewEntry();

        var act = () => entry.Exclude("  ");

        act.Should().Throw<DomainException>();
    }
}
