using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Identity;

/// <summary>
/// Builds a deal's shareable referral link from <see cref="ReferralOptions.BaseUrl"/> by appending
/// the token as a <c>ref</c> query parameter. Falls back to a relative link when no base is
/// configured, so a stack without the setting still returns a usable value.
/// </summary>
public sealed class ReferralLinkBuilder : IReferralLinkBuilder
{
    private readonly ReferralOptions _options;

    public ReferralLinkBuilder(IOptions<ReferralOptions> options) => _options = options.Value;

    public string BuildReferralUrl(string referralToken)
    {
        var encoded = Uri.EscapeDataString(referralToken);
        var baseUrl = _options.BaseUrl;

        if (string.IsNullOrWhiteSpace(baseUrl))
            return $"/invest?ref={encoded}";

        var separator = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl.TrimEnd('/')}{separator}ref={encoded}";
    }
}
