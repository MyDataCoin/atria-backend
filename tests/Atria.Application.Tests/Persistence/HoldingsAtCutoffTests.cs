using Atria.Application.Abstractions;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using Atria.Domain.Trading;
using Atria.Infrastructure.Persistence;
using Atria.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Atria.Application.Tests.Persistence;

/// <summary>
/// «What did this investor hold at the end of the year» — the question a year-end tax statement asks.
/// Today's holdings answer a different question: a stake bought in January was not held in December,
/// and one given up in January still was.
/// </summary>
public sealed class HoldingsAtCutoffTests
{
    private static readonly DateTime Cutoff = new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly Guid _investor = Guid.NewGuid();

    private static AtriaDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AtriaDbContext>()
                .UseInMemoryDatabase($"holdings-{Guid.NewGuid()}")
                .Options,
            new NoOpEncryption());

    private Investment Placement(Property property, long tokens, DateTime activatedAt, string? wallet = null)
    {
        var investment = InvestmentFactory.CreateForInvestor(
            _investor, property.Id, tokens, "KGS", pricePerToken: 100m, reservedUntilUtc: activatedAt.AddDays(3));
        if (wallet is not null)
            investment.SetTokenTarget(wallet, "0xcontract");
        investment.Approve(activatedAt);
        return investment;
    }

    private static ExchangeTrade Trade(Property property, string seller, string buyer, long tokens, DateTime settledAt)
        => ExchangeTrade.Settle(
            $"evt-{Guid.NewGuid()}", 1, $"trade-{Guid.NewGuid()}", property.Id, "bsc", "0xcontract",
            "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), 1,
            seller, buyer, tokens, 150m, "KGS", settledAt, settledAt, null, null, null, settledAt);

    [Fact]
    public async Task Counts_what_was_held_at_the_cutoff_not_what_is_held_now()
    {
        await using var db = NewDb();
        var tower = Property.Create("Tower One", null, null, 1_000_000m, 100m, 10_000, "KGS");
        var annex = Property.Create("Annex", null, null, 1_000_000m, 100m, 10_000, "KGS");
        db.Properties.AddRange(tower, annex);

        var heldAllYear = Placement(tower, 10, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), "0xMINE");
        var boughtAfter = Placement(tower, 5, new DateTime(2027, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        // Withdrawn within its 14 days, but only after the year closed: it was held on 31 December.
        var withdrawnAfter = Placement(annex, 7, new DateTime(2026, 12, 28, 0, 0, 0, DateTimeKind.Utc));
        withdrawnAfter.Withdraw(new DateTime(2027, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        // Annulled during the year: gone by its end.
        var annulledDuring = Placement(annex, 4, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        annulledDuring.Annul("ошибка", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        db.Investments.AddRange(heldAllYear, boughtAfter, withdrawnAfter, annulledDuring);

        // Sold 4 on the secondary market in the year; a later sale does not count.
        db.ExchangeTrades.AddRange(
            Trade(tower, "0xmine", "0xsomeoneelse", 4, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
            Trade(tower, "0xmine", "0xsomeoneelse", 3, new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var holdings = await new InvestmentRepository(db).GetHoldingsAtAsync(_investor, Cutoff, CancellationToken.None);

        holdings.Should().HaveCount(2);
        var t = holdings.Single(h => h.PropertyId == tower.Id);
        t.TokenCount.Should().Be(6);
        t.Amount.Should().Be(600m, "the 4 sold shares leave at their average cost of 100");
        var a = holdings.Single(h => h.PropertyId == annex.Id);
        a.TokenCount.Should().Be(7);
    }

    [Fact]
    public async Task Shares_bought_on_the_secondary_market_are_held_at_their_price()
    {
        await using var db = NewDb();
        var tower = Property.Create("Tower One", null, null, 1_000_000m, 100m, 10_000, "KGS");
        db.Properties.Add(tower);
        db.Investments.Add(Placement(tower, 2, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), "0xmine"));
        db.ExchangeTrades.Add(
            Trade(tower, "0xseller", "0xMine", 3, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var holdings = await new InvestmentRepository(db).GetHoldingsAtAsync(_investor, Cutoff, CancellationToken.None);

        holdings.Single().TokenCount.Should().Be(5);
        holdings.Single().Amount.Should().Be(200m + 3 * 150m);
    }

    private sealed class NoOpEncryption : IEncryptionService
    {
        public string Encrypt(string plaintext) => plaintext;
        public string Decrypt(string ciphertext) => ciphertext;
    }
}
