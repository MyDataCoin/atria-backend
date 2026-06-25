namespace Atria.Application.Investments.Dtos;

/// <summary>Hosted payment session returned to the client to complete a purchase.</summary>
/// <param name="SessionId">Identifier of the payment session at the provider.</param>
/// <param name="PaymentUrl">Hosted checkout URL to redirect the payer to, or <c>null</c> when the provider does not use one.</param>
public sealed record PaymentSessionDto(string SessionId, string? PaymentUrl);
