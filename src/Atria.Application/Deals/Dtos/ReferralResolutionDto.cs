namespace Atria.Application.Deals.Dtos;

/// <summary>
/// Public resolution of a referral token, for the investor landing page. Carries only what a
/// prospective investor may see — the property the link points to and whether it is still live —
/// never the realtor's commission.
/// </summary>
/// <param name="PropertyId">The property the referral link points to.</param>
/// <param name="IsRedeemable">Whether the link is still valid (pending and not expired) right now.</param>
/// <param name="ExpiresAtUtc">UTC instant at which the link stops being valid.</param>
public sealed record ReferralResolutionDto(Guid PropertyId, bool IsRedeemable, DateTime ExpiresAtUtc);
