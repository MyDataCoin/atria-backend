using Atria.Application.Abstractions;
using Atria.Application.Holders.Commands;
using Atria.Domain.Holders;
using Atria.Application.TravelRule;
using Atria.Domain.Investments;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Holders;

/// <summary>
/// The replay is what makes the chain the source of truth: a trade between two investors never
/// passes through the platform, so the register only learns about it by reading transfers back.
/// Where that replay STARTS decides whether it works at all — a live chain is over a hundred million
/// blocks deep, and starting at zero means walking history in which the contract did not exist,
/// through ranges the node refuses to serve.
/// </summary>
public sealed class SyncHolderRegistryFromChainTests
{
    private const string Chain = "bsc-testnet";
    private const string Contract = "0x440bc3b478d6d5c18ec537431e7C3e602E46c088";
    private const long DeploymentBlock = 126_927_728;
    private const long Head = 127_127_787;

    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IHolderPositionRepository _positions = Substitute.For<IHolderPositionRepository>();
    private readonly IChainSyncCursorRepository _cursors = Substitute.For<IChainSyncCursorRepository>();
    private readonly IComplianceRepository _profiles = Substitute.For<IComplianceRepository>();
    private readonly ITokenReader _tokens = Substitute.For<ITokenReader>();
    private readonly ITravelRuleReporter _travelRule = Substitute.For<ITravelRuleReporter>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public SyncHolderRegistryFromChainTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));
        _tokens.GetHeadBlockAsync(Chain, Arg.Any<CancellationToken>()).Returns(Head);
        _tokens.GetTransfersAsync(
                Chain, Contract, Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TokenTransfer>());
    }

    private SyncHolderRegistryFromChainCommandHandler NewHandler() =>
        new(_properties, _positions, _cursors, _profiles, _tokens, _travelRule, _clock, _uow);

    private Property GivenIssue(long? deploymentBlock)
    {
        var property = Property.Create("Borsan Residence", null, null, 7_260_000m, 100m, 72_600, "KGS");
        property.Publish();
        property.SetTokenContract(Contract, Chain, "0xissuer", deploymentBlock);
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        _cursors.FindByPropertyAsync(property.Id, Arg.Any<CancellationToken>())
            .Returns((ChainSyncCursor?)null);

        return property;
    }

    /// <summary>
    /// The window has to open at the contract, not at the chain. Starting at zero was not a slow
    /// path but a dead one: reaching a contract 126 million blocks in would take 25 000 runs, and
    /// the node rejects the oldest ranges before any of them arrive.
    /// </summary>
    [Fact]
    public async Task The_first_window_opens_at_the_deployment_block()
    {
        var property = GivenIssue(DeploymentBlock);

        await NewHandler().Handle(new SyncHolderRegistryFromChainCommand(property.Id), default);

        await _tokens.Received(1).GetTransfersAsync(
            Chain, Contract,
            Arg.Is<long>(from => from == DeploymentBlock),
            Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The deployment block itself carries the contract's creation and, usually, the first mint.
    /// Seeding the cursor AT that block instead of before it would mark it processed while nothing
    /// had been read, and the register would open its history one block too late.
    /// </summary>
    [Fact]
    public async Task The_deployment_block_itself_is_not_skipped()
    {
        var property = GivenIssue(DeploymentBlock);

        // Read at the moment of seeding, not afterwards: the cursor is one object and the run
        // advances it to the end of the window before this test could look at it.
        long? seededAt = null;
        await _cursors.AddAsync(
            Arg.Do<ChainSyncCursor>(c => seededAt = c.LastProcessedBlock), Arg.Any<CancellationToken>());

        await NewHandler().Handle(new SyncHolderRegistryFromChainCommand(property.Id), default);

        seededAt.Should().Be(DeploymentBlock - 1);
    }

    /// <summary>A chain that genuinely starts at zero — a local devnet — still replays from zero.</summary>
    [Fact]
    public async Task An_issue_without_a_deployment_block_still_starts_at_zero()
    {
        var property = GivenIssue(null);

        await NewHandler().Handle(new SyncHolderRegistryFromChainCommand(property.Id), default);

        await _tokens.Received(1).GetTransfersAsync(
            Chain, Contract, Arg.Is<long>(from => from == 0), Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The window never reads closer to the head than the confirmation depth: a transfer that a
    /// reorg could still undo must not become a holding.
    /// </summary>
    [Fact]
    public async Task The_window_stops_short_of_the_unconfirmed_head()
    {
        var property = GivenIssue(DeploymentBlock);

        await NewHandler().Handle(new SyncHolderRegistryFromChainCommand(property.Id), default);

        await _tokens.Received(1).GetTransfersAsync(
            Chain, Contract, Arg.Any<long>(),
            Arg.Is<long>(to => to <= Head - 15), Arg.Any<CancellationToken>());
    }
}
