using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Users.Dtos;

namespace Atria.Application.Users.Queries;

/// <summary>Lists staff (Admin/SuperAdmin) accounts for the super-admin "Admins" tab. SuperAdmin only.</summary>
public sealed record GetAdminsQuery : IRequest<Result<IReadOnlyList<AdminDto>>>;

/// <summary>
/// Projects the staff accounts (Admin + SuperAdmin) into the admins list: id (the ban/password
/// target), username and blocked flag. No staff account yields an empty list (a success, never a 404).
/// </summary>
public sealed class GetAdminsQueryHandler
    : IRequestHandler<GetAdminsQuery, Result<IReadOnlyList<AdminDto>>>
{
    private readonly IUserRepository _users;

    public GetAdminsQueryHandler(IUserRepository users) => _users = users;

    public async Task<Result<IReadOnlyList<AdminDto>>> Handle(GetAdminsQuery request, CancellationToken ct)
    {
        var staff = await _users.GetStaffAsync(ct);

        IReadOnlyList<AdminDto> dtos = staff
            .Select(u => new AdminDto(u.Id, FullName: null, u.Username, Email: null, u.IsBanned))
            .ToList();

        return Result.Success(dtos);
    }
}
