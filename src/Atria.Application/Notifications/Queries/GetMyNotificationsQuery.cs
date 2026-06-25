using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Notifications.Dtos;

namespace Atria.Application.Notifications.Queries;

/// <summary>Lists the current user's notifications.</summary>
public sealed record GetMyNotificationsQuery : IRequest<Result<IReadOnlyList<NotificationDto>>>;

/// <summary>Returns the notifications addressed to the authenticated user.</summary>
public sealed class GetMyNotificationsQueryHandler
    : IRequestHandler<GetMyNotificationsQuery, Result<IReadOnlyList<NotificationDto>>>
{
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUserService _currentUser;

    public GetMyNotificationsQueryHandler(
        INotificationRepository notifications, ICurrentUserService currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<NotificationDto>>> Handle(
        GetMyNotificationsQuery request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure<IReadOnlyList<NotificationDto>>(
                Error.Unauthorized("auth.required", "Authentication is required."));

        var items = await _notifications.GetByUserAsync(userId, ct);

        IReadOnlyList<NotificationDto> dtos = items
            .Select(n => new NotificationDto(n.Id, n.Template, n.Title, n.Body, n.IsRead, n.CreatedAtUtc))
            .ToList();

        return Result.Success(dtos);
    }
}
