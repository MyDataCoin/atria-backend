using System.ComponentModel.DataAnnotations;

namespace Atria.Application.Deals;

/// <summary>
/// Ceiling for the commission a realtor may put on a referral deal.
/// </summary>
/// <remarks>
/// The percent arrives in the create-deal request from the realtor themselves, and the deal settles
/// automatically the moment an investor buys through the link — no one approves the terms in between.
/// Bounded only by the domain invariant (0–100), that let a realtor book the entire purchase as their
/// own commission on every property they linked. The ceiling lives in configuration rather than in
/// code because it is a commercial number, not a technical one: it changes without a rebuild.
/// </remarks>
public sealed class DealCommissionOptions
{
    public const string SectionName = "DealCommission";

    /// <summary>
    /// Highest commission percent a realtor may set on a deal they create. Anything above is
    /// rejected with a validation error naming the limit.
    /// </summary>
    [Range(0, 100)]
    public decimal MaxPercent { get; init; } = 10m;
}
