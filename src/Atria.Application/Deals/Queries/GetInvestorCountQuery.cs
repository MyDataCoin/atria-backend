using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Deals.Queries;

/// <summary>Headline count of registered investors for the realtor dashboard (a number only, no PII).</summary>
public sealed record GetInvestorCountQuery : IRequest<Result<int>>;

/// <summary>Returns how many active investor accounts exist. Exposes only the count — never investor data.</summary>
public sealed class GetInvestorCountQueryHandler : IRequestHandler<GetInvestorCountQuery, Result<int>>
{
    private readonly IUserRepository _users;

    public GetInvestorCountQueryHandler(IUserRepository users) => _users = users;

    public async Task<Result<int>> Handle(GetInvestorCountQuery request, CancellationToken ct)
        => Result.Success(await _users.CountByRoleAsync(Role.Investor, ct));
}
