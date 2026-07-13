namespace Atria.Application.Abstractions;

/// <summary>
/// Builds the public, shareable referral link for a deal's token. The base URL points at the
/// investor-facing frontend and is supplied by configuration, so the Application layer never
/// hard-codes a host.
/// </summary>
public interface IReferralLinkBuilder
{
    /// <summary>The full link an investor opens to invest under a referral (e.g. <c>{base}/invest?ref={token}</c>).</summary>
    string BuildReferralUrl(string referralToken);
}
