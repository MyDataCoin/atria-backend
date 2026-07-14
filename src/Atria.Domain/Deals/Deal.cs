using System.Security.Cryptography;
using Atria.Domain.Common;
using Atria.Domain.Deals.Events;

namespace Atria.Domain.Deals;

/// <summary>
/// A realtor's referral deal: a link (referral token) tied to a specific property that promises the
/// realtor a commission if an investor buys through it. The link lives for <see cref="LinkLifetime"/>;
/// a purchase through it settles the deal <see cref="DealStatus.Successful"/>, otherwise it is
/// <see cref="DealStatus.Rejected"/> once it expires unused.
/// </summary>
public sealed class Deal : AggregateRoot
{
    /// <summary>How long a referral link stays live after creation.</summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(14);

    public Guid RealtorId { get; private set; }
    public Guid PropertyId { get; private set; }

    /// <summary>The realtor's commission as a percentage of the investor's purchase (0–100).</summary>
    public decimal CommissionPercent { get; private set; }

    /// <summary>Opaque, URL-safe referral token embedded in the shareable link. Unique across deals.</summary>
    public string ReferralToken { get; private set; } = null!;

    public DealStatus Status { get; private set; }

    /// <summary>When the referral link stops being valid (creation + <see cref="LinkLifetime"/>).</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>The investment that closed this deal, once it is <see cref="DealStatus.Successful"/>.</summary>
    public Guid? MatchedInvestmentId { get; private set; }

    // private ctor: creation only through the factory method
    private Deal() { }

    /// <summary>
    /// Creates a Pending deal with a freshly generated referral token, live for
    /// <see cref="LinkLifetime"/> from <paramref name="nowUtc"/>.
    /// </summary>
    public static Deal Create(Guid realtorId, Guid propertyId, decimal commissionPercent, DateTime nowUtc)
    {
        if (realtorId == Guid.Empty)
            throw new DomainException("Realtor id is required.");
        if (propertyId == Guid.Empty)
            throw new DomainException("Property id is required.");
        if (commissionPercent is < 0 or > 100)
            throw new DomainException("Commission percent must be between 0 and 100.");

        var deal = new Deal
        {
            Id = Guid.NewGuid(),
            RealtorId = realtorId,
            PropertyId = propertyId,
            CommissionPercent = commissionPercent,
            ReferralToken = GenerateReferralToken(),
            Status = DealStatus.Pending,
            ExpiresAtUtc = nowUtc.Add(LinkLifetime)
        };

        deal.RaiseEvent(new DealCreatedEvent(deal.Id, realtorId, propertyId, commissionPercent));
        return deal;
    }

    /// <summary>Whether the referral link is still usable at <paramref name="nowUtc"/>.</summary>
    public bool IsRedeemable(DateTime nowUtc) => Status == DealStatus.Pending && nowUtc < ExpiresAtUtc;

    /// <summary>
    /// Closes the deal successfully against the investment that was made through the link
    /// (Pending -> Successful). No-op if the deal is already settled.
    /// </summary>
    public void MarkSuccessful(Guid investmentId)
    {
        if (Status != DealStatus.Pending)
            return;

        Status = DealStatus.Successful;
        MatchedInvestmentId = investmentId;
        RaiseEvent(new DealSucceededEvent(Id, RealtorId, PropertyId, investmentId, CommissionPercent));
    }

    /// <summary>Rejects the deal because its link expired unused (Pending -> Rejected). No-op if settled.</summary>
    public void Reject()
    {
        if (Status != DealStatus.Pending)
            return;

        Status = DealStatus.Rejected;
        RaiseEvent(new DealRejectedEvent(Id, RealtorId, PropertyId, CommissionPercent));
    }

    // 32 bytes of entropy, URL-safe base64 (no padding/+//) so it drops straight into a link.
    private static string GenerateReferralToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
