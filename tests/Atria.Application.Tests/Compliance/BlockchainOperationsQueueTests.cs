using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Compliance.Commands;
using Atria.Application.Compliance.Queries;
using Atria.Domain.Audit;
using Atria.Domain.Compliance;
using Atria.Domain.Investments;
using Atria.Domain.Users;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Compliance;

/// <summary>
/// The operations queue is the only window onto what happens after an application is approved —
/// there is no manual mint anywhere, on purpose. These cover what an operator relies on it for:
/// seeing which issue and investor an operation belongs to, how deep a submitted transaction is,
/// why a failed one failed, and getting it running again.
/// </summary>
public sealed class BlockchainOperationsQueueTests
{
    private readonly IBlockchainOperationRepository _operations =
        Substitute.For<IBlockchainOperationRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IKycRepository _kyc = Substitute.For<IKycRepository>();
    private readonly IChainNetworkResolver _networks = Substitute.For<IChainNetworkResolver>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IAuditWriter _audit = Substitute.For<IAuditWriter>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid PropertyId = Guid.NewGuid();
    private static readonly Guid InvestorId = Guid.NewGuid();
    private const string Wallet = "0xBcd4042DE499D14e55001CcbB24a551F3b954096";

    public BlockchainOperationsQueueTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(Role.Admin).Returns(true);
        _networks.ConfirmationsRequired.Returns(15);
        _networks.Resolve("bsc-testnet").Returns(new ChainNetwork(
            "bsc-testnet", 97, "https://rpc", "0xreg", "0xallow", "https://testnet.bscscan.com"));
    }

    private ListBlockchainOperationsQueryHandler NewListHandler() =>
        new(_operations, _properties, _kyc, _networks, _currentUser);

    private RetryBlockchainOperationCommandHandler NewRetryHandler() =>
        new(_operations, _currentUser, _audit, _uow);

    private static BlockchainOperation Allocation(string? payload = null)
        => BlockchainOperation.Create(
            BlockchainOperationType.TokenAllocation,
            payload ?? JsonSerializer.Serialize(new
            {
                investmentId = Guid.NewGuid(),
                investorId = InvestorId,
                propertyId = PropertyId,
                chain = "bsc-testnet",
                wallet = Wallet,
                tokenCount = 100L
            }),
            $"TokenAllocation:{Guid.NewGuid()}");

    private void GivenQueue(params BlockchainOperation[] operations)
        => _operations.ListAsync(Arg.Any<BlockchainOperationStatus?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(operations);

    [Fact]
    public async Task A_row_names_the_issue_the_investor_and_the_shares_it_moves()
    {
        var property = Property.Create("Borsan Residence, кв. 1", null, null, 83_400m, 100m, 834, "KGS");
        _properties.GetByIdAsync(PropertyId, Arg.Any<CancellationToken>()).Returns(property);
        GivenQueue(Allocation());

        var rows = (await NewListHandler().Handle(
            new ListBlockchainOperationsQuery(), CancellationToken.None)).Value;

        var row = rows.Should().ContainSingle().Subject;
        row.Type.Should().Be("token_allocation");
        row.PropertyId.Should().Be(PropertyId);
        row.PropertyName.Should().Be("Borsan Residence, кв. 1");
        row.InvestorId.Should().Be(InvestorId);
        row.WalletAddress.Should().Be(Wallet);
        row.TokenCount.Should().Be(100);
    }

    [Fact]
    public async Task A_submitted_row_carries_its_depth_and_a_link_to_the_explorer()
    {
        // "Submitted" alone cannot tell a transaction three blocks deep from one still in the
        // mempool — the depth is what distinguishes a run that is progressing from a wedged one.
        var operation = Allocation();
        operation.MarkSubmitted("0xdeadbeef");
        operation.ObserveConfirmations(3);
        GivenQueue(operation);

        var row = (await NewListHandler().Handle(
            new ListBlockchainOperationsQuery(), CancellationToken.None)).Value.Single();

        row.Status.Should().Be("submitted");
        row.Confirmations.Should().Be(3);
        row.ConfirmationsRequired.Should().Be(15);
        row.ExplorerUrl.Should().Be("https://testnet.bscscan.com/tx/0xdeadbeef");
    }

    [Fact]
    public async Task A_failed_row_shows_the_error_verbatim()
    {
        var operation = Allocation();
        operation.MarkFailed("insufficient funds for gas * price + value");
        GivenQueue(operation);

        var row = (await NewListHandler().Handle(
            new ListBlockchainOperationsQuery(), CancellationToken.None)).Value.Single();

        row.Status.Should().Be("failed");
        row.Error.Should().Be("insufficient funds for gas * price + value");
    }

    [Fact]
    public async Task A_payload_that_cannot_be_read_still_yields_a_row()
    {
        // An operations screen that 500s on one unexpected payload would break exactly when it is
        // needed, so the row degrades to nulls instead.
        GivenQueue(Allocation(payload: "not json at all"));

        var result = await NewListHandler().Handle(
            new ListBlockchainOperationsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var row = result.Value.Single();
        row.PropertyId.Should().BeNull();
        row.InvestorId.Should().BeNull();
        row.TokenCount.Should().BeNull();
    }

    [Fact]
    public async Task A_network_with_no_explorer_configured_yields_no_link_rather_than_a_broken_one()
    {
        _networks.Resolve("bsc-testnet").Returns(new ChainNetwork(
            "bsc-testnet", 97, "https://rpc", "0xreg", "0xallow", ExplorerBaseUrl: null));
        var operation = Allocation();
        operation.MarkSubmitted("0xdeadbeef");
        GivenQueue(operation);

        var row = (await NewListHandler().Handle(
            new ListBlockchainOperationsQuery(), CancellationToken.None)).Value.Single();

        row.TransactionRef.Should().Be("0xdeadbeef");
        row.ExplorerUrl.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_status_filter_is_refused_rather_than_silently_ignored()
    {
        var result = await NewListHandler().Handle(
            new ListBlockchainOperationsQuery(Status: "stuck"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("operations.unknownStatus");
    }

    [Fact]
    public async Task The_page_size_is_capped_however_large_a_limit_is_asked_for()
    {
        GivenQueue();

        await NewListHandler().Handle(
            new ListBlockchainOperationsQuery(Limit: 10_000), CancellationToken.None);

        await _operations.Received().ListAsync(
            Arg.Any<BlockchainOperationStatus?>(), 200, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_an_admin_may_read_the_queue()
    {
        _currentUser.IsInRole(Role.Admin).Returns(false);

        var result = await NewListHandler().Handle(
            new ListBlockchainOperationsQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("operations.forbidden");
    }

    // ── Retry ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retrying_a_failed_operation_re_queues_it_and_keeps_its_attempt_count()
    {
        var operation = Allocation();
        operation.IncrementAttempt();
        operation.MarkSubmitted("0xdeadbeef");
        operation.MarkFailed("node unreachable");
        _operations.GetByIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(operation);

        var result = await NewRetryHandler().Handle(
            new RetryBlockchainOperationCommand(operation.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        operation.Status.Should().Be(BlockchainOperationStatus.Created);
        operation.Error.Should().BeNull();
        operation.TransactionRef.Should().BeNull();
        operation.Attempts.Should().Be(1, "счётчик попыток — это история, обнулять его нельзя");
        await _audit.Received().WriteAsync(
            AuditEntities.BlockchainOperation, operation.Id, AuditEvents.BlockchainOperationRetried,
            Arg.Any<string>(), Arg.Any<AuditSeverity>(), Arg.Any<CancellationToken>());
        await _uow.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_operation_still_in_flight_is_not_re_queued()
    {
        // Re-queueing a submitted operation would race the worker reconciling the same transaction.
        var operation = Allocation();
        operation.MarkSubmitted("0xdeadbeef");
        _operations.GetByIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(operation);

        var result = await NewRetryHandler().Handle(
            new RetryBlockchainOperationCommand(operation.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("operations.notFailed");
        operation.Status.Should().Be(BlockchainOperationStatus.Submitted);
    }

    [Fact]
    public async Task Only_an_admin_may_retry()
    {
        _currentUser.IsInRole(Role.Admin).Returns(false);

        var result = await NewRetryHandler().Handle(
            new RetryBlockchainOperationCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("operations.forbidden");
    }
}
