using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;
using Atria.Domain.Investments;

namespace Atria.Application.Investments.Commands;

/// <summary>Starts a hosted payment session for the investment created from an approved application.</summary>
public sealed record CreatePaymentSessionCommand(Guid ApplicationId, PaymentProviderType Provider)
    : IRequest<Result<PaymentSessionDto>>;
