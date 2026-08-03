using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>
/// The issue was declared invalid (draft Decree, §73). Terminal: there is nothing to announce,
/// publish or complete, because the issue should never have been placed in the first place.
/// </summary>
public sealed class InvalidatedPropertyState : IPropertyState
{
    public PropertyStatus Status => PropertyStatus.Invalidated;

    public IPropertyState Announce(Property property)
        => throw new InvalidStateTransitionException("An invalidated issue cannot be announced.");

    public IPropertyState Unannounce(Property property)
        => throw new InvalidStateTransitionException("An invalidated issue cannot be unannounced.");

    public IPropertyState Publish(Property property)
        => throw new InvalidStateTransitionException("An invalidated issue cannot be published again.");

    public IPropertyState Complete(Property property)
        => throw new InvalidStateTransitionException("An invalidated issue cannot be completed.");

    public IPropertyState Invalidate(Property property)
        => throw new InvalidStateTransitionException("The issue is already invalidated.");

    public static InvalidatedPropertyState Instance { get; } = new();
    private InvalidatedPropertyState() { }
}
