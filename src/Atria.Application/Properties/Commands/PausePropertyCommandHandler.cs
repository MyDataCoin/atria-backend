using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Compliance;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Suspends the issue (draft Decree, ch. 8): new purchases stop, applications are refused, and — once
/// the issue has a token contract — operations on chain are suspended too. Stopping sales in the
/// database alone would leave holders trading freely while the issue is supposedly suspended.
/// Restricted to Admins.
/// </summary>
public sealed class PausePropertyCommandHandler
    : IRequestHandler<PausePropertyCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly IBlockchainOperationQueue _chain;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public PausePropertyCommandHandler(
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

    public async Task<Result> Handle(PausePropertyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(
                Error.Unauthorized("property.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(
                Error.Forbidden("property.forbidden", "Only administrators can pause sales."));

        var property = await _properties.GetByIdAsync(request.Id, ct);
        if (property is null)
            return Result.Failure(
                Error.NotFound("property.notFound", "Property not found."));

        if (property.SalesPaused)
            return Result.Failure(
                Error.Conflict("property.already_paused", "Sales are already paused for this property."));

        property.PauseSales();
        _properties.Update(property);

        // Only once the issue actually lives on chain. Before deployment there is nothing to suspend.
        if (!string.IsNullOrWhiteSpace(property.TokenContractAddress))
        {
            var payload = JsonSerializer.Serialize(new
            {
                propertyId = property.Id,
                tokenContractAddress = property.TokenContractAddress,
                chain = property.TokenChain
            });

            await _chain.EnqueueAsync(
                BlockchainOperationType.TokenPause, payload, $"token-pause:{property.Id}", ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
