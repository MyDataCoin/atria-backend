using Atria.Domain.Users;

namespace Atria.Application.Abstractions;

/// <summary>
/// User aggregate repository. Adds lookup by the two natural login identifiers
/// (email for password accounts, phone for OTP accounts).
/// </summary>
public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);
    Task<User?> GetByPhoneAsync(string phone, CancellationToken ct);
}
