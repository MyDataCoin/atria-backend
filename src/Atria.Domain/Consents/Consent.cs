using Atria.Domain.Common;

namespace Atria.Domain.Consents;

/// <summary>
/// An immutable record that an investor accepted a specific consent document version.
/// Regulator evidence: WHO (<see cref="UserId"/>), WHAT (<see cref="Type"/> +
/// <see cref="Version"/>) and WHEN (<see cref="Entity.CreatedAtUtc"/>, stamped at insert).
/// Append-only: acceptance is never mutated once recorded.
/// </summary>
public sealed class Consent : AggregateRoot
{
    public Guid UserId { get; private set; }
    public ConsentType Type { get; private set; }

    /// <summary>Version of the consent text the user actually accepted (e.g. "1.0").</summary>
    public string Version { get; private set; } = null!;

    // private ctor: creation only through the factory method
    private Consent() { }

    /// <summary>Records an accepted consent. The acceptance instant is CreatedAtUtc (persistence-stamped).</summary>
    public static Consent Record(Guid userId, ConsentType type, string version)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required to record consent.");
        if (string.IsNullOrWhiteSpace(version))
            throw new DomainException("Consent version is required.");

        return new Consent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Version = version.Trim()
        };
    }
}
