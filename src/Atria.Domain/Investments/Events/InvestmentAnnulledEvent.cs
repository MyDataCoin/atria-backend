using Atria.Domain.Common;

namespace Atria.Domain.Investments.Events;

/// <summary>
/// Raised when an operator voids an application that should not stand. The shares return to the pool;
/// if it had been activated, the registry stops counting them and anything minted is burned.
/// </summary>
/// <param name="WasActive">
/// Whether the application had been activated before it was voided. Decides how much has to be undone
/// — an application that never activated placed nothing, so there is no registry entry and no mint.
/// </param>
public sealed record InvestmentAnnulledEvent(
    Guid InvestmentId,
    Guid InvestorId,
    Guid PropertyId,
    decimal TokenCount,
    string Reason,
    bool WasActive) : DomainEventBase;
