using Atria.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Atria.Api.Startup;

/// <summary>
/// Reports the missing burn capability as a degraded readiness state.
/// </summary>
/// <remarks>
/// <para>
/// Degraded, never unhealthy: the platform serves every other function normally, and marking it
/// unhealthy would pull a working process out of the load balancer over a capability most requests
/// never touch — the outage this design exists to avoid.
/// </para>
/// <para>
/// It is here rather than only in the start-up log because a start-up log is read once, on the day
/// of the deployment, by whoever happened to be watching. A readiness state is visible for as long as
/// the gap lasts, which is the interval that actually matters: withdrawals are refused throughout it.
/// </para>
/// </remarks>
public sealed class BurnCapabilityHealthCheck : IHealthCheck
{
    private readonly IBurnCapability _burn;

    public BurnCapabilityHealthCheck(IBurnCapability burn) => _burn = burn;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(_burn.CanBurn
            ? HealthCheckResult.Healthy("Shares can be burned.")
            : HealthCheckResult.Degraded(
                "Blockchain:TokenSigning:CompliancePrivateKey is not set, so shares cannot be burned. "
                + "Withdrawals under §44 are refused; everything else is serving normally."));
}
