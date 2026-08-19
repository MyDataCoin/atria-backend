using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Feedback.Commands;

/// <summary>Marks a feedback request answered. Admin/SuperAdmin.</summary>
/// <param name="Id">The request id.</param>
public sealed record MarkFeedbackHandledCommand(Guid Id) : IRequest<Result>;

/// <summary>
/// Stamps the request as answered so the inbox can separate open questions from closed ones.
/// Idempotent: the first timestamp is kept. 404 when there is no such request.
/// </summary>
public sealed class MarkFeedbackHandledCommandHandler : IRequestHandler<MarkFeedbackHandledCommand, Result>
{
    private readonly IFeedbackRequestRepository _feedback;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public MarkFeedbackHandledCommandHandler(
        IFeedbackRequestRepository feedback, IDateTimeProvider clock, IUnitOfWork unitOfWork)
    {
        _feedback = feedback;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MarkFeedbackHandledCommand request, CancellationToken ct)
    {
        var feedback = await _feedback.GetByIdAsync(request.Id, ct);
        if (feedback is null)
            return Result.Failure(Error.NotFound("feedback.not_found", "Feedback request not found."));

        feedback.MarkHandled(_clock.UtcNow);
        _feedback.Update(feedback);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
