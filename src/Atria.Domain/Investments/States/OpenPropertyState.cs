using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>Published and open to investors. Can be completed (-> Completed).</summary>
public sealed class OpenPropertyState : IPropertyState
{
    public PropertyStatus Status => PropertyStatus.Open;

    public IPropertyState Announce(Property property) => ComingSoonPropertyState.Instance;

    public IPropertyState Publish(Property property)
        => throw new InvalidStateTransitionException("Property is already open.");

    public IPropertyState Complete(Property property) => CompletedPropertyState.Instance;

    public static OpenPropertyState Instance { get; } = new();
    private OpenPropertyState() { }
}
