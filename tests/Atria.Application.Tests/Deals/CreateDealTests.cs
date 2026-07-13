using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Deals.Commands;
using Atria.Domain.Deals;
using Atria.Domain.Investments;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Deals;

/// <summary>
/// Verifies deal creation: a realtor can only create a deal against an OPEN property, the deal is
/// persisted, and the returned DTO carries the built referral link.
/// </summary>
public sealed class CreateDealTests
{
    private readonly IDealRepository _deals = Substitute.For<IDealRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IReferralLinkBuilder _links = Substitute.For<IReferralLinkBuilder>();

    private CreateDealCommandHandler CreateSut() =>
        new(_deals, _properties, _uow, _currentUser, _clock, _links);

    public CreateDealTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc));
        _links.BuildReferralUrl(Arg.Any<string>()).Returns(ci => $"https://atria.app/invest?ref={ci.Arg<string>()}");
    }

    private static Property OpenProperty()
    {
        var property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 10_000, "USD");
        property.Publish(); // Open
        return property;
    }

    [Fact]
    public async Task Creating_a_deal_for_an_open_property_persists_it_and_returns_the_link()
    {
        var realtorId = Guid.NewGuid();
        _currentUser.UserId.Returns(realtorId);
        var property = OpenProperty();
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);

        var result = await CreateSut().Handle(new CreateDealCommand(property.Id, 5m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PropertyId.Should().Be(property.Id);
        result.Value.CommissionPercent.Should().Be(5m);
        result.Value.Status.Should().Be("pending");
        result.Value.ReferralUrl.Should().Contain("ref=");
        await _deals.Received(1).AddAsync(Arg.Is<Deal>(d => d.RealtorId == realtorId), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Creating_a_deal_for_a_missing_or_non_open_property_is_rejected()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());
        var draft = Property.Create("Draft", null, null, 1_000m, 100m, 10, "USD"); // still Draft
        _properties.GetByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);

        var result = await CreateSut().Handle(new CreateDealCommand(draft.Id, 5m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        await _deals.DidNotReceive().AddAsync(Arg.Any<Deal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unauthenticated_caller_is_rejected()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await CreateSut().Handle(new CreateDealCommand(Guid.NewGuid(), 5m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }
}
