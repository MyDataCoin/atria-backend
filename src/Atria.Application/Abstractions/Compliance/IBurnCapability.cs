namespace Atria.Application.Abstractions;

/// <summary>
/// Whether the platform can currently destroy shares on chain, asked BEFORE anything is promised on
/// the strength of it.
/// </summary>
/// <remarks>
/// <para>
/// Burning is signed by a key the configuration is allowed not to have: the operational-key mode
/// requires the minter and the oracle at start-up but deliberately not the compliance key, because
/// refusing to boot over a capability nothing was using that minute once took production down.
/// The cost of that choice is that the gap is discovered late — <see cref="ITokenGateway.BurnAsync"/>
/// raises it when the burn is already queued, by which point a withdrawal has been accepted, the
/// investment marked withdrawn and a refund obligation raised.
/// </para>
/// <para>
/// That leaves the investor holding both the refund and the shares, and the register disagreeing with
/// the chain until a reconciliation notices. Asking first turns a half-accepted withdrawal into a
/// refusal that changes nothing — the worse of the two outcomes is the one that touches data.
/// </para>
/// <para>
/// A capability, not a health probe: it answers from configuration and does not call the chain, so it
/// is cheap enough to ask on every withdrawal and says nothing about whether the network is reachable
/// this second.
/// </para>
/// </remarks>
public interface IBurnCapability
{
    /// <summary>
    /// True when a burn can be signed. False only for a configuration gap the operator must close,
    /// never for a transient failure.
    /// </summary>
    bool CanBurn { get; }
}
