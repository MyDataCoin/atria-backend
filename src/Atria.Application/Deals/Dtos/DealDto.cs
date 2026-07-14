using Atria.Domain.Deals;

namespace Atria.Application.Deals.Dtos;

/// <summary>Read model of a realtor's referral deal, including its shareable link.</summary>
/// <param name="Id">The deal's unique identifier.</param>
/// <param name="PropertyId">The property the referral link points to.</param>
/// <param name="CommissionPercent">The realtor's commission as a percent of the investor's purchase.</param>
/// <param name="ReferralToken">The opaque referral token embedded in the link.</param>
/// <param name="ReferralUrl">The full shareable link the realtor hands to a prospective investor.</param>
/// <param name="Status">Lifecycle status, lowercase: <c>pending</c> | <c>successful</c> | <c>rejected</c>.</param>
/// <param name="ExpiresAtUtc">UTC instant at which the referral link stops being valid.</param>
/// <param name="MatchedInvestmentId">The investment that closed the deal, once successful; otherwise null.</param>
/// <param name="InvestmentAmount">Amount of the matched investment, in <see cref="Currency"/>; null unless the deal is successful.</param>
/// <param name="Currency">3-letter ISO currency of the matched investment (e.g. <c>KGS</c>); null unless successful.</param>
/// <param name="CommissionAmount">The realtor's earnings on this deal (<c>investmentAmount × commissionPercent / 100</c>), in <see cref="Currency"/>; null unless successful.</param>
public sealed record DealDto(
    Guid Id,
    Guid PropertyId,
    decimal CommissionPercent,
    string ReferralToken,
    string ReferralUrl,
    string Status,
    DateTime ExpiresAtUtc,
    Guid? MatchedInvestmentId,
    decimal? InvestmentAmount,
    string? Currency,
    decimal? CommissionAmount)
{
    /// <summary>Maps the domain status to its lowercase wire value.</summary>
    public static string ToWireStatus(DealStatus status) => status switch
    {
        DealStatus.Successful => "successful",
        DealStatus.Rejected => "rejected",
        _ => "pending"
    };
}
