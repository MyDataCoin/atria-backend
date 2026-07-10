using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>Initial state: created but not yet published. Can be published (-> Open).</summary>
public sealed class DraftPropertyState : IPropertyState
{
    public PropertyStatus Status => PropertyStatus.Draft;

    public IPropertyState Announce(Property property) => ComingSoonPropertyState.Instance;

    public IPropertyState Unannounce(Property property)
        => throw new InvalidStateTransitionException("Property is already a draft.");

    public IPropertyState Publish(Property property) => OpenPropertyState.Instance;

    public IPropertyState Complete(Property property)
        => throw new InvalidStateTransitionException("A draft property must be published before it can be completed.");

    public static DraftPropertyState Instance { get; } = new();
    private DraftPropertyState() { }
}
