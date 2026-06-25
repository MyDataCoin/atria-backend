using Atria.Application.Abstractions;

namespace Atria.Infrastructure.Identity;

/// <summary>Password hashing via BCrypt (adaptive, salted, slow KDF).</summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    // Cost factor 12 — a sensible 2025 default balancing security and latency.
    private const int WorkFactor = 12;

    public string Hash(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        // A malformed/legacy hash must fail closed rather than throw.
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
