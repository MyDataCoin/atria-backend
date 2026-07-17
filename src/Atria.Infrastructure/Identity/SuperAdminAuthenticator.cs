using System.Security.Cryptography;
using System.Text;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Identity;

/// <summary>
/// Validates the static super-admin credentials from <see cref="SuperAdminOptions"/>. The feature is
/// enabled once a <see cref="SuperAdminOptions.Username"/> and <see cref="SuperAdminOptions.UserId"/>
/// are configured — the password is OPTIONAL, because the real password is verified against the
/// seeded <c>users</c> row's hash at login. Config credentials are compared in constant time (no
/// short-circuit) so a wrong username and a wrong password are indistinguishable by timing.
/// </summary>
public sealed class SuperAdminAuthenticator : ISuperAdminAuthenticator
{
    private readonly SuperAdminOptions _options;

    public SuperAdminAuthenticator(IOptions<SuperAdminOptions> options) => _options = options.Value;

    // Enabled by identity (username + id), not by a configured password: login checks the password
    // against the seeded users-row hash. This lets a super admin exist purely as a DB row.
    public bool IsEnabled => !string.IsNullOrEmpty(_options.Username) && _options.UserId != Guid.Empty;

    public Guid SuperAdminUserId => _options.UserId;

    public bool Validate(string username, string password)
    {
        // The config-password path is only a fallback (used when the users row has no hash yet); an
        // empty configured password never matches, so it can't grant a blank-password login.
        if (!IsEnabled || string.IsNullOrEmpty(_options.Password))
            return false;

        // Non-short-circuiting AND: always compare both fields.
        var userOk = FixedTimeEquals(username, _options.Username);
        var passOk = FixedTimeEquals(password, _options.Password);
        return userOk & passOk;
    }

    public bool MatchesUsername(string username)
        => IsEnabled && FixedTimeEquals(username, _options.Username);

    private static bool FixedTimeEquals(string? a, string? b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a ?? string.Empty),
            Encoding.UTF8.GetBytes(b ?? string.Empty));
}
