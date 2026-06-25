using System.Security.Cryptography;
using System.Text;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Identity;

/// <summary>
/// Authenticated at-rest encryption for PII (KYC) fields using AES-256-GCM.
/// On-the-wire format is base64(nonce | tag | ciphertext).
/// </summary>
public sealed class AesGcmEncryptionService : IEncryptionService
{
    private const int KeySizeBytes = 32; // AES-256
    private const int NonceSize = 12;    // AesGcm.NonceByteSizes.MaxSize (recommended GCM nonce)
    private const int TagSize = 16;      // AesGcm.TagByteSizes.MaxSize (full 128-bit auth tag)

    private readonly byte[] _key;

    public AesGcmEncryptionService(IOptions<EncryptionOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.Key);
        if (_key.Length != KeySizeBytes)
            throw new InvalidOperationException(
                $"Encryption key must be {KeySizeBytes} bytes (base64-encoded); got {_key.Length}.");
    }

    public string Encrypt(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plainBytes.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        // Layout: nonce | tag | ciphertext
        var output = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);

        return Convert.ToBase64String(output);
    }

    public string Decrypt(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);
        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext is too short to be valid.");

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipherLength = data.Length - NonceSize - TagSize;
        var cipher = new byte[cipherLength];

        Buffer.BlockCopy(data, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(data, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(data, NonceSize + TagSize, cipher, 0, cipherLength);

        var plain = new byte[cipherLength];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain); // throws on tamper/auth failure

        return Encoding.UTF8.GetString(plain);
    }
}
