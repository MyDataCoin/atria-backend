using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Properties.Commands;
using Atria.Domain.Compliance;
using Atria.Domain.Investments;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Properties;

/// <summary>
/// §16: the appraisal is not merely filed, it is attested on chain. What matters is that only a
/// complete file is attested, and that the commitment covers the whole file rather than a convenient
/// part of it.
/// </summary>
public sealed class SetPropertyCollateralCommandHandlerTests
{
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IBlockchainOperationQueue _chain = Substitute.For<IBlockchainOperationQueue>();
    private readonly IAuditWriter _audit = Substitute.For<IAuditWriter>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly DateTime ValuedAt = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private Property GivenIssue(bool deployed = true)
    {
        var property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 1_000, "KGS");
        property.Publish();
        if (deployed)
            property.SetTokenContract("0xcontract", "bsc-testnet", "0xissuer");

        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        return property;
    }

    private SetPropertyCollateralCommandHandler NewHandler() =>
        new(_properties, _chain, _audit, _uow);

    private static SetPropertyCollateralCommand FullAppraisal(Guid id, decimal value = 1_250_000m) =>
        new(id, value, ValuedAt, "ОсОО «Оценка»", "ОБР-2026-0042", ValuedAt, "ВЫП-2026-0007", null);

    private string? QueuedPayload()
        => _chain.ReceivedCalls()
            .Where(c => (BlockchainOperationType)c.GetArguments()[0]! == BlockchainOperationType.CollateralReport)
            .Select(c => (string)c.GetArguments()[1]!)
            .LastOrDefault();

    [Fact]
    public async Task A_complete_appraisal_is_attested_on_chain()
    {
        var property = GivenIssue();

        var result = await NewHandler().Handle(FullAppraisal(property.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        QueuedPayload().Should().NotBeNull();
        QueuedPayload().Should().Contain("dataHash").And.Contain("1250000");
    }

    /// <summary>Attesting to a half-filled file would put a meaningless commitment on chain.</summary>
    [Fact]
    public async Task An_incomplete_appraisal_is_refused_and_nothing_is_attested()
    {
        var property = GivenIssue();

        var result = await NewHandler().Handle(
            new SetPropertyCollateralCommand(property.Id, 1_250_000m, null, null, null, null, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        QueuedPayload().Should().BeNull();
    }

    [Fact]
    public async Task An_issue_with_no_contract_records_the_file_without_attesting()
    {
        var property = GivenIssue(deployed: false);

        var result = await NewHandler().Handle(FullAppraisal(property.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        property.CollateralValue.Should().Be(1_250_000m);
        QueuedPayload().Should().BeNull("there is no contract to attest to yet");
    }

    /// <summary>
    /// Recording the same appraisal twice must converge on one attestation; a different one is a new
    /// commitment. The idempotency key carries the content hash, which is what makes that work.
    /// </summary>
    [Fact]
    public async Task The_same_appraisal_reuses_its_key_and_a_new_one_gets_its_own()
    {
        var property = GivenIssue();
        var handler = NewHandler();

        await handler.Handle(FullAppraisal(property.Id), CancellationToken.None);
        await handler.Handle(FullAppraisal(property.Id), CancellationToken.None);
        await handler.Handle(FullAppraisal(property.Id, value: 1_400_000m), CancellationToken.None);

        var keys = _chain.ReceivedCalls()
            .Where(c => (BlockchainOperationType)c.GetArguments()[0]! == BlockchainOperationType.CollateralReport)
            .Select(c => (string)c.GetArguments()[2]!)
            .ToList();

        keys.Should().HaveCount(3);
        keys[0].Should().Be(keys[1], "the same appraisal is the same attestation");
        keys[2].Should().NotBe(keys[0], "a different valuation is a different attestation");
    }

    [Fact]
    public async Task Changing_the_appraiser_changes_the_commitment()
    {
        var property = GivenIssue();
        var handler = NewHandler();

        await handler.Handle(FullAppraisal(property.Id), CancellationToken.None);
        await handler.Handle(
            new SetPropertyCollateralCommand(
                property.Id, 1_250_000m, ValuedAt, "Другой оценщик", "ОБР-2026-0042", ValuedAt,
                "ВЫП-2026-0007", null),
            CancellationToken.None);

        var keys = _chain.ReceivedCalls()
            .Where(c => (BlockchainOperationType)c.GetArguments()[0]! == BlockchainOperationType.CollateralReport)
            .Select(c => (string)c.GetArguments()[2]!)
            .ToList();

        keys[0].Should().NotBe(keys[1], "the hash covers the whole file, not just the number");
    }
}
