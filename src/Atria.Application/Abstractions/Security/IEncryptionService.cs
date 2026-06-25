namespace Atria.Application.Abstractions;

/// <summary>
/// Application-level encryption for sensitive KYC/PII fields (encryption at rest).
/// Key material comes from configuration/secret manager, never hard-coded.
/// </summary>
public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
