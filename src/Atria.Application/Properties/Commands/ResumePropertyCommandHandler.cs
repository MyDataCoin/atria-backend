using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Compliance;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Lifts a suspension: purchases resume and, where the issue lives on chain, operations are resumed
/// there too. An invalidated issue can never be resumed. Restricted to Admins.
/// </summary>
public sealed class ResumePropertyCommandHandler
    : IRequestHandler<ResumePropertyCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly IBlockchainOperationQueue _chain;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public ResumePropertyCommandHandler(
        IPropertyRepository properties,
        IBlockchainOperationQueue chain,
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _chain = chain;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ResumePropertyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(
                Error.Unauthorized("property.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(
                Error.Forbidden("property.forbidden", "Only administrators can resume sales."));

        var property = await _properties.GetByIdAsync(request.Id, ct);
        if (property is null)
            return Result.Failure(
                Error.NotFound("property.notFound", "Property not found."));

        if (!property.SalesPaused)
            return Result.Failure(
                Error.Conflict("property.not_paused", "Sales are not paused for this property."));

        // An issue declared invalid is finished. Resuming it would put shares back on sale that are
        // being withdrawn from circulation.
        if (property.Status == PropertyStatus.Invalidated)
            return Result.Failure(Error.Conflict(
                "property.invalidated", "An invalidated issue cannot be resumed."));

        property.ResumeSales();
        _properties.Update(property);

        if (!string.IsNullOrWhiteSpace(property.TokenContractAddress))
        {
            var payload = JsonSerializer.Serialize(new
            {
                propertyId = property.Id,
                tokenContractAddress = property.TokenContractAddress,
                chain = property.TokenChain
            });

            // Keyed by the resume, not by the property, so a later suspension can be lifted again.
            await _chain.EnqueueAsync(
                BlockchainOperationType.TokenUnpause, payload,
                $"token-unpause:{property.Id}:{DateTime.UtcNow:yyyyMMddHHmmss}", ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
