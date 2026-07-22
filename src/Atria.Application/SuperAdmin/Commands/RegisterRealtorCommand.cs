using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.Realtors.Dtos;
using Atria.Domain.Audit;
using Atria.Domain.Realtors;
using Atria.Domain.Users;

namespace Atria.Application.SuperAdmin.Commands;

/// <summary>Registers a new realtor account (users row + realtor profile). Super admin only.</summary>
/// <param name="Username">Login name; must be unique.</param>
/// <param name="Password">Cleartext password set by the super admin; stored hashed.</param>
/// <param name="FullName">Realtor full name for the profile.</param>
/// <param name="CompanyName">Company name (optional).</param>
/// <param name="PhoneNumber">
/// Contact phone (optional). Accepted for forward compatibility but not persisted: the phone column
/// is the unique OTP-login identifier for investors, and credential realtors are keyed by username.
/// </param>
public sealed record RegisterRealtorCommand(
    string Username, string Password, string FullName, string? CompanyName, string? PhoneNumber)
    : IRequest<Result<RealtorStatsDto>>;

/// <summary>
/// Creates a Realtor credential account: a <c>users</c> row (role Realtor, username + password hash)
/// and its <c>realtor_profiles</c> row, in one transaction, and journals the action. Returns a
/// leaderboard-shaped row (zero deals, not blocked) so the client can drop it straight into the list.
/// 409 when the username is taken.
/// </summary>
public sealed class RegisterRealtorCommandHandler
    : IRequestHandler<RegisterRealtorCommand, Result<RealtorStatsDto>>
{
    private readonly IUserRepository _users;
    private readonly IRealtorProfileRepository _profiles;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterRealtorCommandHandler(
        IUserRepository users,
        IRealtorProfileRepository profiles,
        IPasswordHasher passwordHasher,
        IAuditWriter audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _profiles = profiles;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<RealtorStatsDto>> Handle(RegisterRealtorCommand request, CancellationToken ct)
    {
        var username = request.Username.Trim();

        if (await _users.GetByUsernameAsync(username, ct) is not null)
            return Result.Failure<RealtorStatsDto>(
                Error.Conflict("realtor.username_taken", "This username is already taken."));

        var user = User.CreateServiceAccount(username, Role.Realtor, _passwordHasher.Hash(request.Password));
        await _users.AddAsync(user, ct);

        var profile = RealtorProfile.Create(user.Id, request.FullName.Trim(), companyName: request.CompanyName);
        await _profiles.AddAsync(profile, ct);

        await _audit.WriteAsync(
            AuditEntities.User, user.Id, AuditEvents.RealtorRegistered,
            $"Зарегистрирован риелтор «{profile.FullName}» ({username})",
            AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new RealtorStatsDto(
            user.Id, profile.FullName, profile.CompanyName, ClosedDeals: 0, TotalDeals: 0, Blocked: false));
    }
}
