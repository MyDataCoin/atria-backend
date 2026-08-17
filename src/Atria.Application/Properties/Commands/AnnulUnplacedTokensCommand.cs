using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Compliance;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Annuls part of an issue that was never placed (draft Decree, ch. 11). Admin only.
/// </summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="TokenCount">How many unplaced shares to annul.</param>
/// <param name="Reason">Why. Recorded in the journal and carried to the contract operation.</param>
public sealed record AnnulUnplacedTokensCommand(Guid PropertyId, decimal TokenCount, string Reason)
    : IRequest<Result>;

/// <summary>
/// Shrinks the issue by the annulled amount and lowers the cap on the token contract to match, so the
/// registered issue size and what the contract will ever allow stay the same number.
///
/// Only unplaced capacity can be annulled here. Shares already in an investor's hands are not the
/// issuer's to cancel — withdrawing those from circulation is what invalidating an issue does, and it
/// comes with refunds.
/// </summary>
public sealed class AnnulUnplacedTokensCommandHandler : IRequestHandler<AnnulUnplacedTokensCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly IBlockchainOperationQueue _chain;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public AnnulUnplacedTokensCommandHandler(
        IPropertyRepository properties,
        IBlockchainOperationQueue chain,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _properties = properties;
        _chain = chain;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(AnnulUnplacedTokensCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure(Error.Validation("property.reasonRequired", "A reason is required."));

        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("property.notFound", "Property not found."));

        try
        {
            property.AnnulUnplacedTokens(request.TokenCount);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("property.cannotAnnul", ex.Message));
        }

        _properties.Update(property);

        if (!string.IsNullOrWhiteSpace(property.TokenContractAddress))
        {
            var payload = JsonSerializer.Serialize(new
            {
                propertyId = property.Id,
                tokenContractAddress = property.TokenContractAddress,
                chain = property.TokenChain,
                newMaxSupply = property.TotalTokens,
                reason = request.Reason
            });

            // Keyed by the resulting size: re-running the same annulment converges on one operation,
            // while a further annulment is a different one.
            await _chain.EnqueueAsync(
                BlockchainOperationType.TokenReduceSupply, payload,
                $"token-reduce-supply:{property.Id}:{property.TotalTokens}", ct);
        }

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.IssueTokensAnnulled,
            $"Аннулировано {request.TokenCount} неразмещённых долей объекта «{property.Name}»: {request.Reason}",
            AuditSeverity.Warning, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
