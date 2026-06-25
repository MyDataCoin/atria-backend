using Atria.Domain.Common;

namespace Atria.Domain.Outbox;

/// <summary>
/// Transactional outbox row. Written in the SAME transaction as the aggregate
/// that raised the event (see Infrastructure UnitOfWork) so domain events are
/// never lost. A background dispatcher polls unprocessed rows, deserializes the
/// payload, dispatches the event, then calls <see cref="MarkProcessed"/>;
/// failures bump <see cref="Attempts"/> and record the error for backoff/retry.
/// </summary>
public sealed class OutboxMessage : Entity
{
    public Guid EventId { get; private set; }
    public string Type { get; private set; } = default!;   // assembly-qualified event type name
    public string Payload { get; private set; } = default!; // System.Text.Json serialized event
    public DateTime OccurredOnUtc { get; private set; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }

    // Parameterless ctor for EF rehydration only.
    private OutboxMessage() { }

    private OutboxMessage(Guid eventId, string type, string payload, DateTime occurredOnUtc)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        Type = type;
        Payload = payload;
        OccurredOnUtc = occurredOnUtc;
    }

    /// <summary>Create a pending outbox message for a raised domain event.</summary>
    public static OutboxMessage Create(Guid eventId, string type, string payload, DateTime occurredOnUtc)
        => new(eventId, type, payload, occurredOnUtc);

    /// <summary>Mark the message as successfully dispatched; clears any prior error.</summary>
    public void MarkProcessed(DateTime utc)
    {
        ProcessedOnUtc = utc;
        Error = null;
    }

    /// <summary>Record a dispatch failure and increment the attempt counter (for backoff/cap).</summary>
    public void MarkFailed(string error)
    {
        Attempts++;
        Error = error;
    }
}
