using System.Security.Cryptography;
using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.SuperAdmin.Dtos;
using Atria.Domain.Audit;
using Atria.Domain.Users;

namespace Atria.Application.SuperAdmin.Commands;

/// <summary>
/// Resets an admin's or realtor's password. Super admin only. Investors sign in by phone OTP and
/// have no password, so a reset on one is rejected.
/// </summary>
/// <param name="UserId">The <c>users.id</c> whose password to reset.</param>
/// <param name="NewPassword">An explicit password to set; when null a temporary one is generated.</param>
public sealed record ResetUserPasswordCommand(Guid UserId, string? NewPassword)
    : IRequest<Result<ResetPasswordResultDto>>;

/// <summary>
/// Sets a new (explicit or generated) password on the target admin/realtor account, hashes it, flags
/// the account to change it on next use, and journals the action. Returns the plaintext once so the
/// super admin can hand it over. 404 when the user does not exist; 409 for an investor (no password).
/// </summary>
public sealed class ResetUserPasswordCommandHandler
    : IRequestHandler<ResetUserPasswordCommand, Result<ResetPasswordResultDto>>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public ResetUserPasswordCommandHandler(
        IUserRepository users, IPasswordHasher passwordHasher, IAuditWriter audit, IUnitOfWork unitOfWork)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ResetPasswordResultDto>> Handle(
        ResetUserPasswordCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure<ResetPasswordResultDto>(
                Error.NotFound("user.not_found", "User not found."));

        if (user.Role is not (Role.Admin or Role.Realtor or Role.SuperAdmin))
            return Result.Failure<ResetPasswordResultDto>(Error.Conflict(
                "user.no_password", "Only admin and realtor accounts have a password to reset."));

        var newPassword = string.IsNullOrWhiteSpace(request.NewPassword)
            ? GenerateTemporaryPassword()
            : request.NewPassword;

        user.SetPassword(_passwordHasher.Hash(newPassword), mustReset: true);
        _users.Update(user);

        await _audit.WriteAsync(
            AuditEntities.User, user.Id, AuditEvents.PasswordReset,
            $"Сброшен пароль аккаунта ({user.Role})", AuditSeverity.Alert, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new ResetPasswordResultDto(newPassword));
    }

    // 12 URL-safe base64 chars (~9 bytes of entropy) — easy to relay, hard to guess.
    private static string GenerateTemporaryPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(9);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
