namespace Atria.Application.Abstractions;

/// <summary>Hashes and verifies passwords (and similar secrets) with a slow KDF.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
