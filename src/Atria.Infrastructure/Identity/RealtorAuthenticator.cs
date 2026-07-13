using System.Security.Cryptography;
using System.Text;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Identity;

/// <summary>
/// Validates the static realtor credentials from <see cref="RealtorOptions"/>. Disabled unless a
/// password is configured. Both fields are compared in constant time (no short-circuit) so a
/// wrong username and a wrong password are indistinguishable by timing.
/// </summary>
public sealed class RealtorAuthenticator : IRealtorAuthenticator
{
    private readonly RealtorOptions _options;

    public RealtorAuthenticator(IOptions<RealtorOptions> options) => _options = options.Value;

    public bool IsEnabled => !string.IsNullOrEmpty(_options.Password);

    public Guid RealtorUserId => _options.UserId;

    public bool Validate(string username, string password)
    {
        if (!IsEnabled)
            return false;

        // Non-short-circuiting AND: always compare both fields.
        var userOk = FixedTimeEquals(username, _options.Username);
        var passOk = FixedTimeEquals(password, _options.Password);
        return userOk & passOk;
    }

    private static bool FixedTimeEquals(string? a, string? b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a ?? string.Empty),
            Encoding.UTF8.GetBytes(b ?? string.Empty));
}
