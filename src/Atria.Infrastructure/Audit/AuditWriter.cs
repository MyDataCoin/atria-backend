using Atria.Application.Abstractions;
using Atria.Domain.Audit;
using Atria.Domain.Users;

namespace Atria.Infrastructure.Audit;

/// <summary>
/// Writes audit entries for the current request's actor. The row is only ADDED to the unit of work —
/// the calling command commits it, so the audit entry and the action it records share one
/// transaction.
///
/// The actor's display name is denormalized at write time: a realtor's profile name, an investor's
/// verified KYC name, or a role label when neither exists (the admin logs in from static config and
/// has no profile row). Anonymous/background callers produce a null actor.
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly IAuditLogRepository _auditLog;
    private readonly ICurrentUserService _currentUser;
    private readonly IRealtorProfileRepository _realtorProfiles;
    private readonly IKycRepository _kyc;
    private readonly IDateTimeProvider _clock;

    public AuditWriter(
        IAuditLogRepository auditLog,
        ICurrentUserService currentUser,
        IRealtorProfileRepository realtorProfiles,
        IKycRepository kyc,
        IDateTimeProvider clock)
    {
        _auditLog = auditLog;
        _currentUser = currentUser;
        _realtorProfiles = realtorProfiles;
        _kyc = kyc;
        _clock = clock;
    }

    public async Task WriteAsync(
        string entityType,
        Guid? entityId,
        string eventType,
        string summary,
        AuditSeverity severity,
        CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        var actorName = await ResolveActorNameAsync(userId, ct);

        var entry = AuditLogEntry.ForAction(
            entityType, entityId, eventType, userId, actorName, summary, severity, _clock.UtcNow);

        await _auditLog.AddAsync(entry, ct);
    }

    private async Task<string?> ResolveActorNameAsync(Guid? userId, CancellationToken ct)
    {
        if (userId is not { } id)
            return null;

        return _currentUser.Role switch
        {
            Role.Admin => "Администратор",
            Role.SuperAdmin => "Супер-администратор",
            Role.Compliance => "Комплаенс",
            Role.Realtor => (await _realtorProfiles.GetByUserIdAsync(id, ct))?.FullName ?? "Риелтор",
            // An investor's name lives in their (encrypted) KYC profile; unverified ones stay generic.
            Role.Investor => (await _kyc.GetByUserIdAsync(id, ct))?.FullName ?? "Инвестор",
            _ => null
        };
    }
}
