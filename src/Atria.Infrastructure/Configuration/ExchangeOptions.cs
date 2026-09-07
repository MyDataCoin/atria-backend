using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Settings for the licensed trading operator that runs secondary trading in our shares.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no outbound endpoint here. Which operator this will be, and whether they
/// take a webhook from us or expect us to read a queue of theirs, is still open — and inventing a URL
/// would produce a client that looks configured and delivers nothing. What the platform can settle on
/// its own is the inbound half: the secret their events are signed with, and how long a signature
/// stays fresh.
/// </para>
/// <para>
/// Two secrets are supported so a key can be rotated without a delivery gap. During a rotation the
/// operator switches to the new key while both are accepted; the retired one is then removed. Without
/// this, rotating a key means an outage or a window where nothing is verified.
/// </para>
/// </remarks>
public sealed class ExchangeOptions
{
    public const string SectionName = "Exchange";

    /// <summary>Name of the operator, for logs and reports.</summary>
    public string? OperatorName { get; init; }

    /// <summary>
    /// Shared secret their events are signed with. Empty means no operator is connected yet, and
    /// every inbound event is refused — which is the correct behaviour, not a degraded one.
    /// </summary>
    public string? WebhookSecret { get; init; }

    /// <summary>
    /// The previous secret, accepted alongside <see cref="WebhookSecret"/> while a rotation is in
    /// progress. Cleared once the operator has switched over.
    /// </summary>
    public string? PreviousWebhookSecret { get; init; }

    /// <summary>
    /// How far a signed timestamp may be from our clock before the request is treated as a replay.
    /// Five minutes covers ordinary clock drift and retry latency without leaving a captured request
    /// usable for long.
    /// </summary>
    [Range(30, 3600)]
    public int SignatureToleranceSeconds { get; init; } = 300;

    /// <summary>
    /// Addresses the operator holds client shares on. A holder position at one of these is the
    /// operator's omnibus account, not a single investor: counting it as one holder would pay
    /// dividends for many people to one address and misstate the concentration of the issue.
    /// </summary>
    public string[] OmnibusAddresses { get; init; } = [];
}
