using Atria.Application.Abstractions;
using Atria.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(AtriaDbContext db) : base(db) { }

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct)
        => Set.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> GetByPhoneAsync(string phone, CancellationToken ct)
        => Set.FirstOrDefaultAsync(u => u.PhoneNumber == phone, ct);
}
