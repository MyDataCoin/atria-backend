using Atria.Domain.Common;
using Atria.Domain.Holders;
using FluentAssertions;

namespace Atria.Domain.Tests.Holders;

/// <summary>
/// The cursor is what keeps the replay idempotent. Everything here protects one property: a block
/// whose transfers were applied is never applied again.
/// </summary>
public sealed class ChainSyncCursorTests
{
    private static readonly Guid PropertyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Now = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_cursor_starts_where_it_was_told_to()
    {
        var cursor = ChainSyncCursor.StartFor(PropertyId, 1_000, Now);

        cursor.PropertyId.Should().Be(PropertyId);
        cursor.LastProcessedBlock.Should().Be(1_000);
        cursor.LastSyncedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void It_moves_forward_and_records_when()
    {
        var cursor = ChainSyncCursor.StartFor(PropertyId, 1_000, Now);
        var later = Now.AddMinutes(5);

        cursor.AdvanceTo(1_500, later);

        cursor.LastProcessedBlock.Should().Be(1_500);
        cursor.LastSyncedAtUtc.Should().Be(later);
    }

    /// <summary>
    /// Rewinding would replay transfers that were already applied, and every one of them would be
    /// counted twice — the registry would report shares that do not exist.
    /// </summary>
    [Fact]
    public void It_never_moves_backwards()
    {
        var cursor = ChainSyncCursor.StartFor(PropertyId, 1_000, Now);

        var act = () => cursor.AdvanceTo(999, Now);

        act.Should().Throw<DomainException>().WithMessage("*cannot move backwards*");
        cursor.LastProcessedBlock.Should().Be(1_000);
    }

    [Fact]
    public void Staying_on_the_same_block_is_allowed()
    {
        var cursor = ChainSyncCursor.StartFor(PropertyId, 1_000, Now);

        cursor.AdvanceTo(1_000, Now.AddMinutes(1));

        cursor.LastProcessedBlock.Should().Be(1_000);
    }

    [Fact]
    public void A_cursor_needs_an_issue_and_a_non_negative_block()
    {
        ((Action)(() => ChainSyncCursor.StartFor(Guid.Empty, 0, Now)))
            .Should().Throw<DomainException>();

        ((Action)(() => ChainSyncCursor.StartFor(PropertyId, -1, Now)))
            .Should().Throw<DomainException>();
    }
}
