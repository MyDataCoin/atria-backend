using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Feedback;
using Atria.Domain.Users;

namespace Atria.Application.Feedback.Commands;

/// <summary>Submits a question from the public-site feedback form. Anonymous — the sender has no account.</summary>
/// <param name="FullName">The sender's name.</param>
/// <param name="Email">Email to answer on.</param>
/// <param name="Phone">Phone to answer on.</param>
/// <param name="Message">The question.</param>
public sealed record SubmitFeedbackCommand(string FullName, string Email, string Phone, string Message)
    : IRequest<Result<Guid>>;

/// <summary>
/// Persists a feedback request. No auth and nothing is linked to a user: the row holds only what the
/// person typed, and the retention sweep deletes it after
/// <see cref="FeedbackRequest.RetentionDays"/> days. Nothing is emailed anywhere — the question is
/// answered from the super-admin help desk, which reads these rows.
/// </summary>
/// <remarks>
/// Deliberately not journalled to the audit log. The audit journal is the immutable record of what
/// staff did to the system and is kept for years — copying a member of the public's name and phone
/// number into it would put that data somewhere the retention sweep cannot reach.
/// </remarks>
public sealed class SubmitFeedbackCommandHandler : IRequestHandler<SubmitFeedbackCommand, Result<Guid>>
{
    private readonly IFeedbackRequestRepository _feedback;
    private readonly IUnitOfWork _unitOfWork;

    public SubmitFeedbackCommandHandler(IFeedbackRequestRepository feedback, IUnitOfWork unitOfWork)
    {
        _feedback = feedback;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(SubmitFeedbackCommand request, CancellationToken ct)
    {
        // Номер приводим к +996XXXXXXXXX: в панели он превращается в ссылку «позвонить», и разнобой
        // в записи ломает ровно её — оператору пришлось бы вычищать пробелы и скобки руками.
        var feedback = FeedbackRequest.Create(
            request.FullName, request.Email, KyrgyzPhone.Normalize(request.Phone), request.Message);

        await _feedback.AddAsync(feedback, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success(feedback.Id);
    }
}
