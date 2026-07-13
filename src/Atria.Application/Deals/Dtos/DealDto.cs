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
public sealed record DealDto(
    Guid Id,
    Guid PropertyId,
    decimal CommissionPercent,
    string ReferralToken,
    string ReferralUrl,
    string Status,
    DateTime ExpiresAtUtc,
    Guid? MatchedInvestmentId)
{
    /// <summary>Maps the domain status to its lowercase wire value.</summary>
    public static string ToWireStatus(DealStatus status) => status switch
    {
        DealStatus.Successful => "successful",
        DealStatus.Rejected => "rejected",
        _ => "pending"
    };
}
