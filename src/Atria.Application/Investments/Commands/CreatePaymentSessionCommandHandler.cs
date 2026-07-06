using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;

namespace Atria.Application.Investments.Commands;

/// <summary>
/// Resolves the investor's pending investment, picks the requested payment provider
/// Strategy, and creates a hosted payment session. The current investor may only open
/// a session for their OWN investment.
/// </summary>
public sealed class CreatePaymentSessionCommandHandler
    : IRequestHandler<CreatePaymentSessionCommand, Result<PaymentSessionDto>>
{
    private readonly IInvestmentRepository _investments;
    private readonly IKycRepository _kyc;
    private readonly IEnumerable<IPaymentProviderStrategy> _providers;
    private readonly ICurrentUserService _currentUser;

    public CreatePaymentSessionCommandHandler(
        IInvestmentRepository investments,
        IKycRepository kyc,
        IEnumerable<IPaymentProviderStrategy> providers,
        ICurrentUserService currentUser)
    {
        _investments = investments;
        _kyc = kyc;
        _providers = providers;
        _currentUser = currentUser;
    }

    public async Task<Result<PaymentSessionDto>> Handle(CreatePaymentSessionCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<PaymentSessionDto>(
                Error.Unauthorized("payment.unauthorized", "Authentication is required."));

        var investment = await _investments.GetByIdAsync(request.InvestmentId, ct);
        if (investment is null)
            return Result.Failure<PaymentSessionDto>(
                Error.NotFound("investment.notFound", "Investment not found."));

        // Resource-based authorization: an investor may only pay for their own investment.
        if (investment.InvestorId != userId.Value)
            return Result.Failure<PaymentSessionDto>(
                Error.Forbidden("investment.forbidden", "You cannot pay for another investor's investment."));

        // A session only makes sense while the investment still awaits payment.
        if (investment.Status != InvestmentStatus.PendingPayment)
            return Result.Failure<PaymentSessionDto>(
                Error.Conflict("payment.notPending", "This investment is not awaiting payment."));

        // Re-check KYC at payment time: it may have been revoked/rejected after the
        // investment was created, in which case the investor must not proceed to pay.
        var kyc = await _kyc.GetByUserIdAsync(userId.Value, ct);
        if (kyc is null || kyc.Status != KycStatus.Approved)
            return Result.Failure<PaymentSessionDto>(
                Error.Forbidden("payment.kyc_not_approved", "KYC must be approved to make a payment."));

        var provider = _providers.FirstOrDefault(p => p.ProviderType == request.Provider);
        if (provider is null)
            return Result.Failure<PaymentSessionDto>(
                Error.Validation("payment.providerUnsupported", "The requested payment provider is not supported."));

        var session = await provider.CreateSessionAsync(
            new PaymentRequest(investment.Id, investment.InvestorId, investment.Amount, investment.Currency, null), ct);

        return new PaymentSessionDto(session.SessionId, session.PaymentUrl);
    }
}
