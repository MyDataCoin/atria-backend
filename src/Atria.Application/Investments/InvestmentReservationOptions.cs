using System.ComponentModel.DataAnnotations;

namespace Atria.Application.Investments;

/// <summary>
/// Reservation policy for offering applications: how long a Reserved application holds its tokens
/// before a background sweep returns them to the pool, and how that sweep is paced. Shared by the
/// application handler (which stamps <see cref="Domain.Investments.Investment.ReservedUntilUtc"/> at creation) and the
/// infrastructure background service (which reclaims lapsed reservations).
/// </summary>
public sealed class InvestmentReservationOptions
{
    public const string SectionName = "InvestmentReservation";

    /// <summary>
    /// How long a reservation is held while awaiting operator approval before its tokens may be
    /// returned to the pool. Generous because approval is a manual back-office action, not a payment
    /// window.
    /// </summary>
    [Range(1, 365)]
    public int WindowDays { get; init; } = 3;

    /// <summary>How often the background sweep looks for lapsed reservations to reclaim.</summary>
    [Range(1, 1440)]
    public int SweepIntervalMinutes { get; init; } = 15;

    /// <summary>
    /// Maximum lapsed reservations reclaimed per sweep, so one large backlog cannot block the sweep in
    /// a single unit of work. Remaining items are picked up on the next tick.
    /// </summary>
    [Range(1, 1000)]
    public int SweepBatchSize { get; init; } = 100;

    /// <summary>The reservation window as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Window => TimeSpan.FromDays(WindowDays);

    /// <summary>The sweep interval as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan SweepInterval => TimeSpan.FromMinutes(SweepIntervalMinutes);
}
