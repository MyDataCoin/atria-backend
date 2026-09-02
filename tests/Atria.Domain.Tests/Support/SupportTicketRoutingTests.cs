using Atria.Domain.Support;
using Atria.Domain.Users;
using FluentAssertions;

namespace Atria.Domain.Tests.Support;

/// <summary>
/// Covers the object a ticket is about — what the desk routes on.
/// <para>
/// The management company staffs one person per object, so a question about a specific building has
/// to carry which building it is. It stays optional on purpose: "I cannot log in" belongs to nobody's
/// property, and forcing every ticket to name one would route platform questions to an owner.
/// </para>
/// </summary>
public sealed class SupportTicketRoutingTests
{
    private static readonly Guid Investor = Guid.NewGuid();
    private static readonly Guid Property = Guid.NewGuid();

    [Fact]
    public void Open_KeepsTheObjectTheQuestionIsAbout()
    {
        var ticket = SupportTicket.Open(
            Investor, "Когда выплата?", "Выплаты", "Вопрос по объекту", Role.Investor, Property);

        ticket.PropertyId.Should().Be(Property);
    }

    [Fact]
    public void Open_LeavesTheObjectUnsetForAPlatformQuestion()
    {
        var ticket = SupportTicket.Open(Investor, "Не приходит СМС", "Доступ", "Не могу войти");

        ticket.PropertyId.Should().BeNull();
    }

    [Fact]
    public void Open_TreatsAnEmptyGuidAsNoObject()
    {
        // A sloppy caller sending Guid.Empty means "none", not a ticket about property 00000000-…
        var ticket = SupportTicket.Open(
            Investor, "Вопрос", "Общее", "Текст", Role.Investor, Guid.Empty);

        ticket.PropertyId.Should().BeNull();
    }
}
