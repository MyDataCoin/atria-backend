using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Compliance;
using Atria.Domain.Users;

namespace Atria.Application.Compliance.Commands;

/// <summary>
/// Puts a failed on-chain operation back in the queue for the worker to submit again. Admin only.
/// </summary>
/// <remarks>
/// Operations fail for reasons that are fixed outside the platform — a minter wallet out of gas, an
/// unreachable node, a nonce clash. Once the cause is dealt with, the work still has to be redone,
/// and the alternative to this is editing a status column by hand.
///
/// It resets the operation to <see cref="BlockchainOperationStatus.Created"/> rather than creating a
/// new one, so the idempotency key stays with it: a retry that produced a second record could submit
/// the same mint twice if the first one turned out to have landed after all.
/// </remarks>
/// <param name="OperationId">The failed operation to re-queue.</param>
public sealed record RetryBlockchainOperationCommand(Guid OperationId) : IRequest<Result>;

public sealed class RetryBlockchainOperationCommandHandler
    : IRequestHandler<RetryBlockchainOperationCommand, Result>
{
    private readonly IBlockchainOperationRepository _operations;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public RetryBlockchainOperationCommandHandler(
        IBlockchainOperationRepository operations,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _operations = operations;
        _currentUser = currentUser;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(RetryBlockchainOperationCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(Error.Unauthorized("operations.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(Error.Forbidden(
                "operations.forbidden", "Only administrators can retry an operation."));

        var operation = await _operations.GetByIdAsync(request.OperationId, ct);
        if (operation is null)
            return Result.Failure(Error.NotFound("operations.notFound", "Operation not found."));

        // Only a failed operation is safe to re-queue. A submitted one may still be in flight, and
        // re-queueing it would race the worker's own reconciliation of the same transaction.
        if (operation.Status is not BlockchainOperationStatus.Failed)
            return Result.Failure(Error.Conflict(
                "operations.notFailed",
                $"Only a failed operation can be retried; this one is {operation.Status}."));

        operation.Requeue();
        _operations.Update(operation);

        await _audit.WriteAsync(
            AuditEntities.BlockchainOperation, operation.Id, AuditEvents.BlockchainOperationRetried,
            $"Операция {operation.Type} поставлена в очередь заново после сбоя",
            AuditSeverity.Warning, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
