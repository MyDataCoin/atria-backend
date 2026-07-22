using Atria.Application.Abstractions;
using Atria.Application.Appeals.Dtos;
using Atria.Application.Common;

namespace Atria.Application.Appeals.Queries;

/// <summary>Lists ban appeals for the super-admin panel, newest first. SuperAdmin only.</summary>
public sealed record GetAppealsQuery : IRequest<Result<IReadOnlyList<AppealDto>>>;

/// <summary>
/// Projects appeals (newest first) with the appellant's full name resolved best-effort from the
/// username. No appeals yields an empty list (a success, never a 404).
/// </summary>
public sealed class GetAppealsQueryHandler
    : IRequestHandler<GetAppealsQuery, Result<IReadOnlyList<AppealDto>>>
{
    private readonly IAppealRepository _appeals;

    public GetAppealsQueryHandler(IAppealRepository appeals) => _appeals = appeals;

    public async Task<Result<IReadOnlyList<AppealDto>>> Handle(GetAppealsQuery request, CancellationToken ct)
    {
        var rows = await _appeals.GetAllWithNamesAsync(ct);

        IReadOnlyList<AppealDto> dtos = rows
            .Select(r => new AppealDto(r.Appeal.Id, r.Appeal.Username, r.FullName, r.Appeal.Message, r.Appeal.CreatedAtUtc))
            .ToList();

        return Result.Success(dtos);
    }
}
