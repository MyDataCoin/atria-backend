namespace Atria.Domain.Common;

/// <summary>
/// Base type for all domain entities. Identity-based equality.
/// Timestamps are written by the persistence layer (DbContext interceptor),
/// not by callers, so they have non-public setters.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; }
    public DateTime CreatedAtUtc { get; protected set; }
    public DateTime? UpdatedAtUtc { get; protected set; }

    public override bool Equals(object? obj)
        => obj is Entity other
           && other.GetType() == GetType()
           && Id != Guid.Empty
           && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);
    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
