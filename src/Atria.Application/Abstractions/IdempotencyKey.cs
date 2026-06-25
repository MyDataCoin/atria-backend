namespace Atria.Application.Abstractions;

/// <summary>
/// Builds the exactly-once dedupe key recorded in <see cref="IProcessedEventStore"/>.
/// The handler's concrete type name scopes the key so the same event id processed by
/// different handlers never collides.
/// </summary>
internal static class IdempotencyKey
{
    public static string For(object handler, object eventId) => $"{handler.GetType().Name}:{eventId}";
}
