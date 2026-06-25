namespace Atria.Application.Investments.Dtos;

/// <summary>Hosted payment session returned to the client to complete a purchase.</summary>
public sealed record PaymentSessionDto(string SessionId, string? PaymentUrl);
