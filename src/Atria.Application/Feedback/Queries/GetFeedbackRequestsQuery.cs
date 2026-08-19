using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Feedback.Dtos;

namespace Atria.Application.Feedback.Queries;

/// <summary>Lists public-site feedback requests, newest first. Admin/SuperAdmin.</summary>
public sealed record GetFeedbackRequestsQuery : IRequest<Result<IReadOnlyList<FeedbackRequestDto>>>;

/// <summary>Projects feedback requests for the panel inbox. No requests yields an empty list, not a 404.</summary>
public sealed class GetFeedbackRequestsQueryHandler
    : IRequestHandler<GetFeedbackRequestsQuery, Result<IReadOnlyList<FeedbackRequestDto>>>
{
    private readonly IFeedbackRequestRepository _feedback;

    public GetFeedbackRequestsQueryHandler(IFeedbackRequestRepository feedback) => _feedback = feedback;

    public async Task<Result<IReadOnlyList<FeedbackRequestDto>>> Handle(
        GetFeedbackRequestsQuery request, CancellationToken ct)
    {
        var rows = await _feedback.GetAllAsync(ct);

        IReadOnlyList<FeedbackRequestDto> dtos = rows
            .Select(f => new FeedbackRequestDto(
                f.Id, f.FullName, f.Contact, f.Message, f.CreatedAtUtc, f.HandledAtUtc))
            .ToList();

        return Result.Success(dtos);
    }
}
