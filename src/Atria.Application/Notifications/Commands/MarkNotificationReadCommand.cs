using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Notifications.Commands;

/// <summary>Marks one of the current user's notifications as read.</summary>
public sealed record MarkNotificationReadCommand(Guid Id) : IRequest<Result>;

/// <summary>
/// Loads the notification, verifies it belongs to the caller, then marks it read
/// at the current instant (idempotent on the aggregate).
/// </summary>
public sealed class MarkNotificationReadCommandHandler
    : IRequestHandler<MarkNotificationReadCommand, Result>
{
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public MarkNotificationReadCommandHandler(
        INotificationRepository notifications,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _notifications = notifications;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MarkNotificationReadCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure(Error.Unauthorized("auth.required", "Authentication is required."));

        var notification = await _notifications.GetByIdAsync(request.Id, ct);
        if (notification is null)
            return Result.Failure(Error.NotFound("notification.not_found", "Notification not found."));

        // Resource-based authorization: only the owner may mark it read.
        if (notification.UserId != userId)
            return Result.Failure(Error.Forbidden("notification.forbidden", "You may not modify this notification."));

        notification.MarkRead(_clock.UtcNow);
        _notifications.Update(notification);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
