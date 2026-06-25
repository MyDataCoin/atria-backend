using Atria.Domain.Notifications;

namespace Atria.Application.Notifications.Dtos;

/// <summary>Read model for a user-facing notification.</summary>
public sealed record NotificationDto(
    Guid Id,
    NotificationTemplate Template,
    string Title,
    string Body,
    bool IsRead,
    DateTime CreatedAtUtc);
