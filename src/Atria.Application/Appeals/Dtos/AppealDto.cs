namespace Atria.Application.Appeals.Dtos;

/// <summary>One ban appeal in the super-admin "Appeals" list.</summary>
/// <param name="Id">Appeal id.</param>
/// <param name="Username">The username the sender tried to log in with.</param>
/// <param name="FullName">Appellant's full name if resolvable from the username; otherwise null.</param>
/// <param name="Message">The appeal text.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the appeal was submitted.</param>
public sealed record AppealDto(
    Guid Id,
    string Username,
    string? FullName,
    string Message,
    DateTime CreatedAtUtc);
