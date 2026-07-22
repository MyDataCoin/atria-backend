using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Appeals;
using Atria.Domain.Audit;

namespace Atria.Application.Appeals.Commands;

/// <summary>Submits a ban appeal from the blocked screen. Anonymous (the sender cannot log in).</summary>
/// <param name="Username">The login the sender tried to use (best-effort link to an account).</param>
/// <param name="Message">The appeal text (required).</param>
public sealed record SubmitAppealCommand(string? Username, string Message) : IRequest<Result<Guid>>;

/// <summary>
/// Persists an anonymous ban appeal and journals it. No auth: a banned user has no token. The message
/// is required (400 otherwise); the username is stored as-is for the super admin to match up.
/// </summary>
public sealed class SubmitAppealCommandHandler : IRequestHandler<SubmitAppealCommand, Result<Guid>>
{
    private readonly IAppealRepository _appeals;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public SubmitAppealCommandHandler(IAppealRepository appeals, IAuditWriter audit, IUnitOfWork unitOfWork)
    {
        _appeals = appeals;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(SubmitAppealCommand request, CancellationToken ct)
    {
        var appeal = Appeal.Create(request.Username, request.Message);
        await _appeals.AddAsync(appeal, ct);

        var who = string.IsNullOrEmpty(appeal.Username) ? "неизвестный аккаунт" : $"«{appeal.Username}»";
        await _audit.WriteAsync(
            AuditEntities.User, appeal.Id, AuditEvents.BanAppealSubmitted,
            $"Обращение о разблокировке ({who})", AuditSeverity.Warning, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success(appeal.Id);
    }
}
