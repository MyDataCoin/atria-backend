namespace Atria.Application.Abstractions;

/// <summary>Credential-login lockout policy for Admin / Realtor / SuperAdmin accounts.</summary>
/// <remarks>
/// Per-IP throttling in the request pipeline does not cover credential stuffing: an attacker with a
/// pool of addresses spends one attempt per address and never trips a per-IP window. The limit that
/// has to hold is the one on the account itself, which is what this configures. It is a policy
/// rather than a constant so an operator can tighten it without a deploy — and so the integration
/// suite, which shares one seeded admin across every test class, can raise it out of the way.
/// </remarks>
public interface IAuthLockoutPolicy
{
    /// <summary>Consecutive wrong passwords before the account is locked.</summary>
    int MaxFailedLogins { get; }

    /// <summary>How long a locked account stays locked.</summary>
    TimeSpan LockoutDuration { get; }
}
