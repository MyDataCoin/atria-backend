using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Payments.Dtos;
using Atria.Domain.Common;
using Atria.Domain.Payments;

namespace Atria.Application.Payments.Commands;

/// <summary>Reads the operator's crypto payment settings.</summary>
public sealed record GetPaymentSettingsQuery : IRequest<Result<PaymentSettingsDto>>;

public sealed class GetPaymentSettingsQueryHandler
    : IRequestHandler<GetPaymentSettingsQuery, Result<PaymentSettingsDto>>
{
    private readonly IPaymentSettingsRepository _settings;

    public GetPaymentSettingsQueryHandler(IPaymentSettingsRepository settings) => _settings = settings;

    public async Task<Result<PaymentSettingsDto>> Handle(GetPaymentSettingsQuery request, CancellationToken ct)
        => Result.Success(PaymentSettingsDto.From(await _settings.GetAsync(ct)));
}

/// <summary>Replaces the crypto payment settings. An empty field switches the methods that need it off.</summary>
public sealed record UpdatePaymentSettingsCommand(
    decimal? UsdtRateKgs,
    string? Bep20Address,
    string? Trc20Address,
    string? KgstContractAddress) : IRequest<Result<PaymentSettingsDto>>;

public sealed class UpdatePaymentSettingsCommandHandler
    : IRequestHandler<UpdatePaymentSettingsCommand, Result<PaymentSettingsDto>>
{
    private readonly IPaymentSettingsRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public UpdatePaymentSettingsCommandHandler(
        IPaymentSettingsRepository settings, IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result<PaymentSettingsDto>> Handle(UpdatePaymentSettingsCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure<PaymentSettingsDto>(
                Error.Unauthorized("payments.unauthorized", "Authentication required."));

        var settings = await _settings.GetAsync(ct);
        var isNew = settings is null;
        settings ??= PaymentSettings.CreateEmpty();

        try
        {
            settings.Update(
                request.UsdtRateKgs, request.Bep20Address, request.Trc20Address, request.KgstContractAddress, userId);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PaymentSettingsDto>(Error.Validation("payments.invalid_settings", ex.Message));
        }

        if (isNew)
            await _settings.AddAsync(settings, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success(PaymentSettingsDto.From(settings));
    }
}

/// <summary>The crypto methods offered right now, with their rates. Anonymous: the site shows them before sign-in.</summary>
public sealed record GetPaymentMethodsQuery : IRequest<Result<IReadOnlyList<PaymentMethodDto>>>;

public sealed class GetPaymentMethodsQueryHandler
    : IRequestHandler<GetPaymentMethodsQuery, Result<IReadOnlyList<PaymentMethodDto>>>
{
    private readonly IPaymentSettingsRepository _settings;

    public GetPaymentMethodsQueryHandler(IPaymentSettingsRepository settings) => _settings = settings;

    public async Task<Result<IReadOnlyList<PaymentMethodDto>>> Handle(GetPaymentMethodsQuery request, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        if (settings is null)
            return Result.Success<IReadOnlyList<PaymentMethodDto>>(Array.Empty<PaymentMethodDto>());

        IReadOnlyList<PaymentMethodDto> methods = settings.EnabledMethods()
            .Select(m => new PaymentMethodDto(m.ToWire(), m.Token(), m.Network(), settings.RateFor(m)!.Value))
            .ToList();
        return Result.Success(methods);
    }
}
