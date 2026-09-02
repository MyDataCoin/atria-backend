using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.Users.Dtos;
using Atria.Domain.Audit;
using Atria.Domain.Users;

namespace Atria.Application.SuperAdmin.Commands;

/// <summary>Creates a staff credential account. Super admin only.</summary>
/// <param name="Username">Login name; must be unique across credential accounts.</param>
/// <param name="FullName">Full name shown in the panel header and built into the avatar initials.</param>
/// <param name="Password">
/// The one-time password the super admin hands over. The account is flagged to change it on first
/// sign-in, so this value is never a long-lived credential.
/// </param>
/// <param name="Role">
/// Which staff account to create: <see cref="Domain.Users.Role.Admin"/> (the default),
/// <see cref="Domain.Users.Role.Finance"/> for an accountant, or
/// <see cref="Domain.Users.Role.Auditor"/> for a lawyer. Realtors are created through their own
/// endpoint, which also takes a company and a phone.
/// </param>
public sealed record RegisterAdminCommand(
    string Username, string FullName, string Password, Role Role = Role.Admin)
    : IRequest<Result<AdminDto>>;

/// <summary>
/// Creates the <c>users</c> row for a new staff account (username + full name + password hash),
/// flags it <c>MustResetPassword</c> so the handed-over password only survives the first sign-in,
/// and journals the action. 409 when the username is taken.
/// </summary>
/// <remarks>
/// The role is restricted here rather than left to whatever the request asks for. This endpoint
/// creates back-office staff, and the set it may create is the set the super admin's panel manages:
/// Admin, Finance (the management company's accountant) and Auditor (its lawyer). Anything else —
/// a second SuperAdmin above all, or an Investor row carrying a password — has to be a deliberate
/// act somewhere else, not a field in a form.
/// </remarks>
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
        if (request.Role is not (Role.Admin or Role.Finance or Role.Auditor))
            return Result.Failure<AdminDto>(Error.Validation(
                "admin.role_not_allowed",
                "A staff account can only be created as Admin, Finance or Auditor."));

        var username = request.Username.Trim();

        if (await _users.GetByUsernameAsync(username, ct) is not null)
            return Result.Failure<AdminDto>(
                Error.Conflict("admin.username_taken", "This username is already taken."));

        var user = User.CreateServiceAccount(
            username,
            request.Role,
            _passwordHasher.Hash(request.Password),
            fullName: request.FullName,
            mustResetPassword: true);

        await _users.AddAsync(user, ct);

        // The role goes in the journal line: "создан сотрудник" says nothing an audit needs, and
        // an accountant's account and an administrator's are very different things to have created.
        var what = request.Role switch
        {
            Role.Finance => "бухгалтер",
            Role.Auditor => "юрист",
            _ => "администратор"
        };

        await _audit.WriteAsync(
            AuditEntities.User, user.Id, AuditEvents.AdminRegistered,
            $"Создан {what} «{user.FullName}» ({username})",
            AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new AdminDto(
            user.Id, user.FullName, user.Username, Email: null, Blocked: false,
            Role: user.Role.ToString()));
    }
}
