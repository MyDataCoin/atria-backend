using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.Users.Dtos;
using Atria.Domain.Audit;
using Atria.Domain.Users;

namespace Atria.Application.SuperAdmin.Commands;

/// <summary>Creates a staff (Admin) credential account. Super admin only.</summary>
/// <param name="Username">Login name; must be unique across credential accounts.</param>
/// <param name="FullName">Full name shown in the panel header and built into the avatar initials.</param>
/// <param name="Password">
/// The one-time password the super admin hands over. The account is flagged to change it on first
/// sign-in, so this value is never a long-lived credential.
/// </param>
public sealed record RegisterAdminCommand(string Username, string FullName, string Password)
    : IRequest<Result<AdminDto>>;

/// <summary>
/// Creates the <c>users</c> row for a new admin (role Admin, username + full name + password hash),
/// flags it <c>MustResetPassword</c> so the handed-over password only survives the first sign-in,
/// and journals the action. 409 when the username is taken.
/// </summary>
public sealed class RegisterAdminCommandHandler : IRequestHandler<RegisterAdminCommand, Result<AdminDto>>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterAdminCommandHandler(
        IUserRepository users, IPasswordHasher passwordHasher, IAuditWriter audit, IUnitOfWork unitOfWork)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AdminDto>> Handle(RegisterAdminCommand request, CancellationToken ct)
    {
        var username = request.Username.Trim();

        if (await _users.GetByUsernameAsync(username, ct) is not null)
            return Result.Failure<AdminDto>(
                Error.Conflict("admin.username_taken", "This username is already taken."));

        var user = User.CreateServiceAccount(
            username,
            Role.Admin,
            _passwordHasher.Hash(request.Password),
            fullName: request.FullName,
            mustResetPassword: true);

        await _users.AddAsync(user, ct);

        await _audit.WriteAsync(
            AuditEntities.User, user.Id, AuditEvents.AdminRegistered,
            $"Создан администратор «{user.FullName}» ({username})",
            AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new AdminDto(user.Id, user.FullName, user.Username, Email: null, Blocked: false));
    }
}
