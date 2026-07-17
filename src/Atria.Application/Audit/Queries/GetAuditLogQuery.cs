using Atria.Application.Abstractions;
using Atria.Application.Audit.Dtos;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Users;

namespace Atria.Application.Audit.Queries;

/// <summary>
/// Reads a page of the audit journal, newest first. Optional filters combine with AND. Restricted to
/// Admin and Compliance roles.
/// </summary>
/// <param name="EntityType">Scope to one aggregate kind (e.g. <c>Property</c>).</param>
/// <param name="EntityId">Scope to one instance.</param>
/// <param name="EventType">Scope to one action (e.g. <c>PropertyPublished</c>).</param>
/// <param name="Severity">Scope to one criticality: <c>success</c> | <c>warning</c> | <c>alert</c>.</param>
/// <param name="Page">1-based page number; defaults to 1.</param>
/// <param name="PageSize">Items per page; defaults to 50, capped at 200.</param>
public sealed record GetAuditLogQuery(
    string? EntityType = null,
    Guid? EntityId = null,
    string? EventType = null,
    string? Severity = null,
    int? Page = null,
    int? PageSize = null) : IRequest<Result<PagedResult<AuditLogDto>>>;

/// <summary>Handles <see cref="GetAuditLogQuery"/>.</summary>
public sealed class GetAuditLogQueryHandler
    : IRequestHandler<GetAuditLogQuery, Result<PagedResult<AuditLogDto>>>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly IAuditLogRepository _repository;
    private readonly ICurrentUserService _currentUser;

    public GetAuditLogQueryHandler(IAuditLogRepository repository, ICurrentUserService currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<AuditLogDto>>> Handle(GetAuditLogQuery request, CancellationToken ct)
    {
        // Role-based authorization: only Admin or Compliance may read the audit trail.
        if (!_currentUser.IsAuthenticated)
            return Result.Failure<PagedResult<AuditLogDto>>(
                Error.Unauthorized("Audit.Unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin) && !_currentUser.IsInRole(Role.Compliance)
            && !_currentUser.IsInRole(Role.SuperAdmin))
            return Result.Failure<PagedResult<AuditLogDto>>(
                Error.Forbidden("Audit.Forbidden", "Only Admin, Compliance or SuperAdmin may read the audit log."));

        AuditSeverity? severity = null;
        if (!string.IsNullOrWhiteSpace(request.Severity))
        {
            if (!AuditLogDto.TryParseSeverity(request.Severity, out var parsed))
                return Result.Failure<PagedResult<AuditLogDto>>(Error.Validation(
                    "audit.invalid_severity", "Severity must be one of: success, warning, alert."));
            severity = parsed;
        }

        var page = Math.Max(1, request.Page ?? 1);
        var pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize);

        var filter = new AuditLogFilter(
            request.EntityType, request.EntityId, request.EventType, severity);
        var (entries, totalCount) = await _repository.GetPageAsync(filter, page, pageSize, ct);

        IReadOnlyList<AuditLogDto> items = entries.Select(AuditLogDto.From).ToList();

        return Result.Success(new PagedResult<AuditLogDto>(items, page, pageSize, totalCount));
    }
}
