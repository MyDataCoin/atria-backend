using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Atria.Application.Abstractions;
using Atria.Domain.Users;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Atria.Infrastructure.Identity;

/// <summary>
/// Issues HS256-signed access tokens (sub/email/role claims) and opaque,
/// cryptographically-random refresh tokens. No blockchain keys are held here.
/// </summary>
public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _options;
    private readonly IDateTimeProvider _clock;

    public JwtTokenGenerator(IOptions<JwtOptions> options, IDateTimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public AccessToken GenerateAccessToken(Guid userId, string email, Role role)
    {
        var now = _clock.UtcNow;
        var expiresAtUtc = now.AddMinutes(_options.AccessTokenMinutes);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            // Emit the role under the literal "role" claim so it matches the API's
            // RoleClaimType="role" (MapInboundClaims=false). Using ClaimTypes.Role here
            // would put it under the long schema URI and break [Authorize(Roles=...)].
            new("role", role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(encoded, expiresAtUtc);
    }

    public GeneratedRefreshToken GenerateRefreshToken()
    {
        // 256 bits of entropy, URL-safe base64 (no padding/+//) so it is transport-safe.
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncoder.Encode(bytes);
        var expiresAtUtc = _clock.UtcNow.AddDays(_options.RefreshTokenDays);
        return new GeneratedRefreshToken(token, expiresAtUtc);
    }
}
