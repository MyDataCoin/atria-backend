using Atria.Domain.Users;

namespace Atria.Application.Abstractions;

/// <summary>
/// User aggregate repository. Lookup is by phone number — the sole login identifier
/// (accounts authenticate via phone OTP).
/// </summary>
public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByPhoneAsync(string phone, CancellationToken ct);
}
