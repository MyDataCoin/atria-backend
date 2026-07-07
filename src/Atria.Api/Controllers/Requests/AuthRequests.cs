using Atria.Domain.Consents;
using Atria.Domain.Documents;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;

namespace Atria.Api.Controllers.Requests;

// HTTP request bodies. Kept separate from Application commands so the wire shape can
// evolve independently and so multipart/route-bound inputs (IFormFile, phone OTP, IP) map cleanly.

/// <summary>POST /auth/refresh body.</summary>
/// <param name="RefreshToken">A valid, unexpired refresh token previously issued by an auth endpoint.</param>
public sealed record RefreshTokenRequest(string RefreshToken);

/// <summary>POST /auth/register/phone/request-otp body. The IP is captured server-side.</summary>
/// <param name="Phone">Kyrgyz phone number in <c>+996XXXXXXXXX</c> format, e.g. <c>+996700123456</c>.</param>
public sealed record RequestOtpRequest(string Phone);

/// <summary>POST /auth/register/phone/verify-otp body.</summary>
/// <param name="Phone">The same Kyrgyz phone number used to request the code, e.g. <c>+996700123456</c>.</param>
/// <param name="Code">The one-time code received via SMS (a fixed dev code in development).</param>
public sealed record VerifyOtpRequest(string Phone, string Code);

/// <summary>POST /kyc/submit body.</summary>
/// <param name="Provider">The KYC verification provider to open a session with.</param>
/// <param name="WalletAddress">Optional 0x-prefixed 40-hex-character wallet address for token allocation.</param>
/// <param name="FullName">Optional full legal name (max 256 chars).</param>
/// <param name="DocumentNumber">Optional identity document number (max 128 chars).</param>
/// <param name="Nationality">Optional nationality (max 128 chars).</param>
public sealed record SubmitKycRequest(
    KycProviderType Provider,
    string? WalletAddress,
    string? FullName,
    string? DocumentNumber,
    string? Nationality);

/// <summary>PATCH /kyc/wallet body. Links the caller's wallet to their KYC profile after verification.</summary>
/// <param name="WalletAddress">0x-prefixed 40-hex-character wallet address for token allocation.</param>
public sealed record LinkWalletRequest(string WalletAddress);

/// <summary>POST /kyc/{id}/review body. <c>Approve=false</c> requires a <c>Reason</c>.</summary>
/// <param name="Approve"><c>true</c> to approve the profile; <c>false</c> to reject it.</param>
/// <param name="Reason">Required when rejecting; the human-readable rejection reason.</param>
public sealed record ReviewKycRequest(bool Approve, string? Reason);

/// <summary>POST /investments body.</summary>
/// <param name="PropertyId">Identifier of the property to invest in.</param>
/// <param name="Amount">Amount the investor wishes to commit; must be greater than 0.</param>
public sealed record CreateInvestmentRequest(Guid PropertyId, decimal Amount);

/// <summary>POST /consent body. Records the caller's acceptance of a consent document version.</summary>
/// <param name="Type">The consent type, sent by name (e.g. <c>Pdn</c> for the personal-data notice).</param>
/// <param name="Version">Version of the consent text the user accepted (e.g. <c>1.0</c>).</param>
/// <param name="Accepted">Must be <c>true</c>; the endpoint only records acceptance.</param>
public sealed record RecordConsentRequest(ConsentType Type, string Version, bool Accepted);

/// <summary>POST /properties body.</summary>
/// <param name="Name">Display name of the property; required, max 256 characters.</param>
/// <param name="Description">Optional longer description; max 4000 characters.</param>
/// <param name="Address">Optional physical address; max 512 characters.</param>
/// <param name="TotalValue">Total monetary value of the property; must be greater than 0.</param>
/// <param name="TokenPrice">Price of a single token; must be greater than 0.</param>
/// <param name="TotalTokens">Total number of tokens to issue; must be greater than 0.</param>
/// <param name="Currency">3-letter ISO currency code (e.g. USD, KGS).</param>
public sealed record CreatePropertyRequest(
    string Name,
    string? Description,
    string? Address,
    decimal TotalValue,
    decimal TokenPrice,
    long TotalTokens,
    string Currency);

/// <summary>POST /investments/{applicationId}/payments body.</summary>
/// <param name="Provider">Payment provider to create the session with, sent by name (for example <c>Stripe</c> or <c>BankTransfer</c>).</param>
public sealed record CreatePaymentRequest(PaymentProviderType Provider);

/// <summary>POST /documents multipart form. The file is bound from the request part.</summary>
/// <param name="File">The document file uploaded as a multipart/form-data part.</param>
/// <param name="Type">Kind of document being uploaded, sent by name.</param>
public sealed record UploadDocumentRequest(IFormFile File, DocumentType Type);
