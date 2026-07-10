using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>
/// Stateless factory mapping the persisted <see cref="PropertyStatus"/> enum to its singleton
/// state object. Keeps EF rehydration to a single column (no _state field).
/// </summary>
public static class PropertyStateFactory
{
    public static IPropertyState Create(PropertyStatus status) => status switch
    {
        PropertyStatus.Draft => DraftPropertyState.Instance,
        PropertyStatus.ComingSoon => ComingSoonPropertyState.Instance,
        PropertyStatus.Open => OpenPropertyState.Instance,
        PropertyStatus.Completed => CompletedPropertyState.Instance,
        _ => throw new InvalidStateTransitionException($"Unknown property status: {status}.")
    };
}
