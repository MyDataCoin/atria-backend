using Atria.Domain.Notifications;

namespace Atria.Application.Notifications.Dtos;

/// <summary>Read model for a single user-facing notification.</summary>
/// <param name="Id">Unique identifier of the notification.</param>
/// <param name="Template">Template the notification was rendered from (serialized by name, e.g. <c>KycApproved</c>).</param>
/// <param name="Title">Short, already-localized title shown to the user.</param>
/// <param name="Body">Already-localized body text shown to the user.</param>
/// <param name="IsRead">Whether the owner has marked the notification as read.</param>
/// <param name="CreatedAtUtc">UTC timestamp at which the notification was created.</param>
public sealed record NotificationDto(
    Guid Id,
    NotificationTemplate Template,
    string Title,
    string Body,
    bool IsRead,
    DateTime CreatedAtUtc);
