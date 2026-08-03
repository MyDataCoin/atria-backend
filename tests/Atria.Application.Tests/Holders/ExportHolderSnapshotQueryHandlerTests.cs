using System.Text;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Holders.Queries;
using Atria.Domain.Holders;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Holders;

/// <summary>
/// The export is what an operator hands to a regulator, so it is rendered server-side and must be
/// reproducible: same snapshot in, same bytes out.
/// </summary>
public sealed class ExportHolderSnapshotQueryHandlerTests
{
    private readonly IHolderSnapshotRepository _snapshots = Substitute.For<IHolderSnapshotRepository>();

    private static readonly Guid PropertyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OperatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid InvestorA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Cut = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);

    private const string WalletA = "0xaaaa000000000000000000000000000000000000";
    private const string WalletB = "0xbbbb000000000000000000000000000000000000";

    private HolderSnapshot GivenSnapshot()
    {
        var snapshot = HolderSnapshot.Create(
            PropertyId, Cut, SnapshotPurpose.Reporting, null, OperatorId,
            new[]
            {
                new HolderSnapshotEntry(WalletB, 250, null),
                new HolderSnapshotEntry(WalletA, 750, InvestorA)
            });
        _snapshots.GetWithRowsAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(snapshot);
        return snapshot;
    }

    private static string Text(byte[] content) => new UTF8Encoding(true).GetString(content).TrimStart('﻿');

    [Fact]
    public async Task Csv_lists_every_row_in_address_order_with_header()
    {
        var snapshot = GivenSnapshot();

        var result = await new ExportHolderSnapshotQueryHandler(_snapshots)
            .Handle(new ExportHolderSnapshotQuery(snapshot.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var lines = Text(result.Value.Content).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be("wallet_address,token_count,share,investor_id");
        lines[1].Should().Be($"{WalletA},750,0.75000000,{InvestorA}");
        lines[2].Should().Be($"{WalletB},250,0.25000000,"); // unlinked address: empty investor id
        result.Value.ContentType.Should().Be("text/csv; charset=utf-8");
        result.Value.FileName.Should().Be($"holder-snapshot-{PropertyId}-20260803T120000Z.csv");
    }

    [Fact]
    public async Task Exporting_the_same_snapshot_twice_yields_identical_bytes()
    {
        var snapshot = GivenSnapshot();
        var handler = new ExportHolderSnapshotQueryHandler(_snapshots);

        var first = await handler.Handle(new ExportHolderSnapshotQuery(snapshot.Id), CancellationToken.None);
        var second = await handler.Handle(new ExportHolderSnapshotQuery(snapshot.Id), CancellationToken.None);

        second.Value.Content.Should().Equal(first.Value.Content);
        second.Value.FileName.Should().Be(first.Value.FileName);
    }

    [Fact]
    public async Task Exporting_an_unknown_snapshot_is_a_not_found()
    {
        _snapshots.GetWithRowsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HolderSnapshot?)null);

        var result = await new ExportHolderSnapshotQueryHandler(_snapshots)
            .Handle(new ExportHolderSnapshotQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
