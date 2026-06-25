using Atria.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Atria.Infrastructure.Persistence;

/// <summary>
/// EF Core value converter that encrypts a string property at rest using the
/// application <see cref="IEncryptionService"/> (AES-GCM). Used for PII columns
/// (e.g. KycProfile.FullName / DocumentNumber). Null values are passed through.
/// </summary>
public sealed class EncryptedConverter : ValueConverter<string, string>
{
    public EncryptedConverter(IEncryptionService encryption)
        : base(
            plaintext => encryption.Encrypt(plaintext),
            ciphertext => encryption.Decrypt(ciphertext))
    {
    }
}
