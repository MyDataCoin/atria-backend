using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Compliance;
using Atria.Domain.Investments;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Records what backs an issue and how it is registered. Every field is optional so the collateral
/// file can be filled in as the documents arrive, but an appraisal is all-or-nothing: a value without
/// its date and appraiser is not evidence of anything.
/// </summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="CollateralValue">Appraised value of the collateral, in the issue's currency.</param>
/// <param name="CollateralValuedAtUtc">Date of that appraisal.</param>
/// <param name="CollateralAppraiser">Who certified it.</param>
/// <param name="EncumbranceRegistrationNumber">Registration number of the encumbrance in the state register.</param>
/// <param name="EncumbranceRegisteredAtUtc">When the encumbrance was registered.</param>
/// <param name="IssueRegistrationNumber">State registration number of the issue itself.</param>
/// <param name="CollateralManagerUserId">The user acting as collateral manager for this issue.</param>
public sealed record SetPropertyCollateralCommand(
    Guid PropertyId,
    decimal? CollateralValue,
    DateTime? CollateralValuedAtUtc,
    string? CollateralAppraiser,
    string? EncumbranceRegistrationNumber,
    DateTime? EncumbranceRegisteredAtUtc,
    string? IssueRegistrationNumber,
    Guid? CollateralManagerUserId) : IRequest<Result>;

public sealed class SetPropertyCollateralCommandHandler
    : IRequestHandler<SetPropertyCollateralCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly IBlockchainOperationQueue _chain;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public SetPropertyCollateralCommandHandler(
        IPropertyRepository properties, IBlockchainOperationQueue chain, IAuditWriter audit, IUnitOfWork uow)
    {
        _properties = properties;
        _chain = chain;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(SetPropertyCollateralCommand request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("property.notFound", "Property not found."));

        var appraisalParts = new object?[]
        {
            request.CollateralValue, request.CollateralValuedAtUtc, request.CollateralAppraiser
        };
        var givenParts = appraisalParts.Count(p => p is not null && p is not string { Length: 0 });
        if (givenParts is > 0 and < 3)
            return Result.Failure(Error.Validation(
                "property.incompleteAppraisal",
                "An appraisal needs its value, its date and its appraiser together."));

        try
        {
            if (givenParts == 3)
                property.SetCollateralAppraisal(
                    request.CollateralValue!.Value, request.CollateralValuedAtUtc!.Value, request.CollateralAppraiser!);

            if (!string.IsNullOrWhiteSpace(request.EncumbranceRegistrationNumber))
                property.SetEncumbrance(
                    request.EncumbranceRegistrationNumber,
                    request.EncumbranceRegisteredAtUtc ?? DateTime.UtcNow);

            if (!string.IsNullOrWhiteSpace(request.IssueRegistrationNumber))
                property.SetIssueRegistrationNumber(request.IssueRegistrationNumber);

            if (request.CollateralManagerUserId is not null)
                property.SetCollateralManager(request.CollateralManagerUserId);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Validation("property.invalidCollateral", ex.Message));
        }

        _properties.Update(property);

        // §16: the appraisal is not just filed, it is delivered into the contract. Only once there is
        // a complete appraisal and a deployed contract — attesting to a half-filled file would put a
        // meaningless commitment on chain that nobody can retract.
        if (property.CollateralValue is not null
            && property.CollateralValuedAtUtc is not null
            && !string.IsNullOrWhiteSpace(property.TokenContractAddress))
        {
            var payload = JsonSerializer.Serialize(new
            {
                propertyId = property.Id,
                chain = property.TokenChain,
                tokenContractAddress = property.TokenContractAddress,
                dataHash = Convert.ToHexString(CollateralDataHash(property)).ToLowerInvariant(),
                valuation = property.CollateralValue,
                valuedAtUtc = property.CollateralValuedAtUtc,
                uri = string.Empty
            });

            // Keyed by the content: re-recording the same appraisal converges on one operation, while
            // a new appraisal is a new attestation.
            await _chain.EnqueueAsync(
                BlockchainOperationType.CollateralReport, payload,
                $"collateral-report:{property.Id}:{Convert.ToHexString(CollateralDataHash(property))[..16]}", ct);
        }

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.PropertyUpdated,
            $"Обновлены сведения об обеспечении объекта «{property.Name}»",
            AuditSeverity.Success, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// A 32-byte commitment to the collateral file as recorded: value, appraisal date, appraiser and
    /// the registered encumbrance.
    /// </summary>
    /// <remarks>
    /// Anyone holding the same documents can recompute it and see whether the platform attested to
    /// the same facts. A hash over a subset — the value alone, say — would let the appraiser or the
    /// encumbrance change without the on-chain commitment moving.
    /// </remarks>
    private static byte[] CollateralDataHash(Property property)
    {
        var canonical = string.Join('|',
            property.Id,
            property.CollateralValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            property.Currency,
            property.CollateralValuedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            property.CollateralAppraiser ?? string.Empty,
            property.EncumbranceRegistrationNumber ?? string.Empty,
            property.EncumbranceRegisteredAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);

        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }
}
