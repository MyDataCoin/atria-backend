using Atria.Domain.Documents;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;

namespace Atria.Api.Controllers.Requests;

// HTTP request bodies. Kept separate from Application commands so the wire shape can
// evolve independently and so multipart/route-bound inputs (IFormFile, phone OTP, IP) map cleanly.

/// <summary>POST /auth/register body.</summary>
public sealed record RegisterRequest(string Email, string Password, string? FirstName, string? LastName);

/// <summary>POST /auth/login body.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>POST /auth/refresh body.</summary>
public sealed record RefreshTokenRequest(string RefreshToken);

/// <summary>POST /auth/register/phone/request-otp body. The IP is captured server-side.</summary>
public sealed record RequestOtpRequest(string Phone);

/// <summary>POST /auth/register/phone/verify-otp body.</summary>
public sealed record VerifyOtpRequest(string Phone, string Code);

/// <summary>POST /kyc/submit body.</summary>
public sealed record SubmitKycRequest(
    KycProviderType Provider,
    string? WalletAddress,
    string? FullName,
    string? DocumentNumber,
    string? Nationality);

/// <summary>POST /kyc/{id}/review body. <c>Approve=false</c> requires a <c>Reason</c>.</summary>
public sealed record ReviewKycRequest(bool Approve, string? Reason);

/// <summary>POST /applications body.</summary>
public sealed record CreateApplicationRequest(Guid PropertyId, decimal Amount);

/// <summary>POST /applications/{id}/reject body.</summary>
public sealed record RejectApplicationRequest(string Reason);

/// <summary>POST /properties body.</summary>
public sealed record CreatePropertyRequest(
    string Name,
    string? Description,
    string? Address,
    decimal TotalValue,
    decimal TokenPrice,
    long TotalTokens,
    string Currency);

/// <summary>POST /investments/{applicationId}/payments body.</summary>
public sealed record CreatePaymentRequest(PaymentProviderType Provider);

/// <summary>POST /documents multipart form. The file is bound from the request part.</summary>
public sealed record UploadDocumentRequest(IFormFile File, DocumentType Type);
