using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>Terminal state: the offering is finished. No further transitions.</summary>
public sealed class CompletedPropertyState : IPropertyState
{
    public PropertyStatus Status => PropertyStatus.Completed;

    public IPropertyState Announce(Property property)
        => throw new InvalidStateTransitionException("A completed property cannot be announced as coming soon.");

    public IPropertyState Publish(Property property)
        => throw new InvalidStateTransitionException("A completed property cannot be published again.");

    public IPropertyState Complete(Property property)
        => throw new InvalidStateTransitionException("Property is already completed.");

    public static CompletedPropertyState Instance { get; } = new();
    private CompletedPropertyState() { }
}
