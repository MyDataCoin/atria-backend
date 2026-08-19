using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Feedback;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Feedback.Commands;

/// <summary>Submits a question from the public-site feedback form. Anonymous — the sender has no account.</summary>
/// <param name="FullName">The sender's name.</param>
/// <param name="Contact">Email or phone to answer on.</param>
/// <param name="Message">The question.</param>
public sealed record SubmitFeedbackCommand(string FullName, string Contact, string Message)
    : IRequest<Result<Guid>>;

/// <summary>
/// Persists a feedback request. No auth and nothing is linked to a user: the row holds only what the
/// person typed, and the retention sweep deletes it after
/// <see cref="FeedbackRequest.RetentionDays"/> days.
/// </summary>
/// <remarks>
/// Deliberately not journalled to the audit log. The audit journal is the immutable record of what
/// staff did to the system and is kept for years — copying a member of the public's name and phone
/// number into it would put that data somewhere the retention sweep cannot reach.
/// <para>
/// The question is stored first and mailed second, and a failed send is logged rather than returned.
/// A mail server that is down must not answer the visitor with an error that invites them to send
/// the same question again: the request is already saved and visible in the panel, so the only thing
/// a failure costs is the notification.
/// </para>
/// </remarks>
public sealed class SubmitFeedbackCommandHandler : IRequestHandler<SubmitFeedbackCommand, Result<Guid>>
{
    private readonly IFeedbackRequestRepository _feedback;
    private readonly IFeedbackMailer _mailer;
    private readonly ILogger<SubmitFeedbackCommandHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public SubmitFeedbackCommandHandler(
        IFeedbackRequestRepository feedback,
        IFeedbackMailer mailer,
        ILogger<SubmitFeedbackCommandHandler> logger,
        IUnitOfWork unitOfWork)
    {
        _feedback = feedback;
        _mailer = mailer;
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(SubmitFeedbackCommand request, CancellationToken ct)
    {
        var feedback = FeedbackRequest.Create(request.FullName, request.Contact, request.Message);
        await _feedback.AddAsync(feedback, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        try
        {
            await _mailer.NotifyAsync(feedback, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to email feedback request {FeedbackId}; it is saved and visible in the panel.", feedback.Id);
        }

        return Result.Success(feedback.Id);
    }
}
