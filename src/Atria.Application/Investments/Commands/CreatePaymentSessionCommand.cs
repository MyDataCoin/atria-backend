using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;
using Atria.Domain.Investments;

namespace Atria.Application.Investments.Commands;

/// <summary>Starts a hosted payment session for one of the investor's pending investments.</summary>
public sealed record CreatePaymentSessionCommand(Guid InvestmentId, PaymentProviderType Provider)
    : IRequest<Result<PaymentSessionDto>>;
