using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Users.Dtos;

namespace Atria.Application.Users.Queries;

/// <summary>Lists all users joined with their (optional) KYC profile. Admin/Compliance only.</summary>
public sealed record GetUsersOverviewQuery : IRequest<Result<IReadOnlyList<UserOverviewDto>>>;

/// <summary>
/// Reads every user left-joined to their KYC profile and projects the overview row.
/// The KYC FullName is decrypted transparently by the EF value converter when the
/// profile entity is materialized in the repository.
/// </summary>
public sealed class GetUsersOverviewQueryHandler
    : IRequestHandler<GetUsersOverviewQuery, Result<IReadOnlyList<UserOverviewDto>>>
{
    private readonly IUserRepository _users;
    private readonly IInvestmentRepository _investments;

    public GetUsersOverviewQueryHandler(IUserRepository users, IInvestmentRepository investments)
    {
        _users = users;
        _investments = investments;
    }

    public async Task<Result<IReadOnlyList<UserOverviewDto>>> Handle(GetUsersOverviewQuery request, CancellationToken ct)
    {
        var rows = await _users.GetOverviewAsync(ct);
        var tokens = await _investments.GetActiveTokenTotalsByInvestorAsync(ct);

        IReadOnlyList<UserOverviewDto> dtos = rows
            .Select(r => new UserOverviewDto(
                r.User.Id,
                r.User.PhoneNumber,
                r.Kyc?.FullName,
                r.Kyc?.WalletAddress,
                r.Kyc?.Status,
                r.Kyc?.Id,
                r.User.Role,
                r.User.IsBanned,
                r.User.CreatedAtUtc,
                tokens.TryGetValue(r.User.Id, out var t) ? t : 0))
            .ToList();

        return Result.Success(dtos);
    }
}
