namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Settings for realtor referral links. The base URL points at the investor-facing frontend; the
/// referral token is appended as a query parameter so the built link opens the investment flow
/// pre-tagged with the referral.
/// </summary>
public sealed class ReferralOptions
{
    public const string SectionName = "Referral";

    /// <summary>
    /// Absolute base of the investor landing URL, e.g. <c>https://atria.app/invest</c>. The token is
    /// appended as <c>?ref={token}</c>.
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;
}
