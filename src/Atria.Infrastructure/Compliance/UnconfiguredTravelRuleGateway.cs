using Atria.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// The travel-rule gateway in the state the platform is actually in: obligations are assembled and
/// queued, and there is nowhere to send them yet.
/// </summary>
/// <remarks>
/// This exists instead of leaving the port unimplemented so that the rest of the system is complete
/// and honest at the same time. Assembly, the queue and the audit trail all work; delivery reports
/// that it did not happen, rather than a no-op silently marking messages sent. The day a counterparty
/// is chosen, this is replaced by an adapter for their protocol and the queued backlog goes out.
/// </remarks>
public sealed class UnconfiguredTravelRuleGateway : ITravelRuleGateway
{
    private readonly ILogger<UnconfiguredTravelRuleGateway> _logger;

    public UnconfiguredTravelRuleGateway(ILogger<UnconfiguredTravelRuleGateway> logger)
        => _logger = logger;

    public bool IsConfigured => false;

    public Task<TravelRuleDeliveryResult> SendAsync(
        string counterpartyVasp, string payload, CancellationToken ct)
    {
        _logger.LogWarning(
            "Travel rule: a disclosure is owed to {Counterparty} but no counterparty service "
            + "provider is configured; the message stays in the queue.",
            counterpartyVasp);

        return Task.FromResult(TravelRuleDeliveryResult.Failure(
            "No counterparty service provider is configured."));
    }
}
