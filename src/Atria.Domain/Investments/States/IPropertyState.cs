namespace Atria.Domain.Investments.States;

/// <summary>
/// State pattern for a <see cref="Property"/> (EF-friendly variant). State objects are stateless
/// singletons; the transition methods encapsulate the allowed moves and return the next state.
/// Only the <see cref="PropertyStatus"/> enum is persisted; the state is derived from it.
/// </summary>
public interface IPropertyState
{
    PropertyStatus Status { get; }

    /// <summary>Draft -> ComingSoon: teases the property on the public site before it opens.</summary>
    IPropertyState Announce(Property property);

    /// <summary>Draft or ComingSoon -> Open: publishes the offering, opening it to investors.</summary>
    IPropertyState Publish(Property property);

    /// <summary>Open -> Completed: closes the offering.</summary>
    IPropertyState Complete(Property property);
}
