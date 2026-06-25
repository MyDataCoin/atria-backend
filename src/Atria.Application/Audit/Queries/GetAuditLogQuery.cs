using Atria.Application.Abstractions;
using Atria.Application.Audit.Dtos;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Audit.Queries;

/// <summary>
/// Reads the audit log, optionally filtered by entity type and/or entity id.
/// Restricted to Admin and Compliance roles.
/// </summary>
public sealed record GetAuditLogQuery(string? EntityType, Guid? EntityId)
    : IRequest<Result<IReadOnlyList<AuditLogDto>>>;

/// <summary>Handles <see cref="GetAuditLogQuery"/>.</summary>
public sealed class GetAuditLogQueryHandler
    : IRequestHandler<GetAuditLogQuery, Result<IReadOnlyList<AuditLogDto>>>
{
    private readonly IAuditLogRepository _repository;
    private readonly ICurrentUserService _currentUser;

    public GetAuditLogQueryHandler(IAuditLogRepository repository, ICurrentUserService currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<AuditLogDto>>> Handle(GetAuditLogQuery request, CancellationToken ct)
    {
        // Role-based authorization: only Admin or Compliance may read the audit trail.
        if (!_currentUser.IsAuthenticated)
        {
            return Result.Failure<IReadOnlyList<AuditLogDto>>(
                Error.Unauthorized("Audit.Unauthorized", "Authentication is required."));
        }

        if (!_currentUser.IsInRole(Role.Admin) && !_currentUser.IsInRole(Role.Compliance))
        {
            return Result.Failure<IReadOnlyList<AuditLogDto>>(
                Error.Forbidden("Audit.Forbidden", "Only Admin or Compliance may read the audit log."));
        }

        var entries = await _repository.QueryAsync(request.EntityType, request.EntityId, ct);

        IReadOnlyList<AuditLogDto> dtos = entries
            .Select(e => new AuditLogDto(
                e.EntityType,
                e.EntityId,
                e.EventType,
                e.DataJson,
                e.UserId,
                e.OccurredOnUtc))
            .ToList();

        return Result.Success(dtos);
    }
}
