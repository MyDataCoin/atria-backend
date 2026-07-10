using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>Announced and teased on the public site ("coming soon"). Can be published (-> Open).</summary>
public sealed class ComingSoonPropertyState : IPropertyState
{
    public PropertyStatus Status => PropertyStatus.ComingSoon;

    public IPropertyState Announce(Property property)
        => throw new InvalidStateTransitionException("Property is already announced as coming soon.");

    public IPropertyState Unannounce(Property property) => DraftPropertyState.Instance;

    public IPropertyState Publish(Property property) => OpenPropertyState.Instance;

    public IPropertyState Complete(Property property)
        => throw new InvalidStateTransitionException("A coming-soon property must be published before it can be completed.");

    public static ComingSoonPropertyState Instance { get; } = new();
    private ComingSoonPropertyState() { }
}
