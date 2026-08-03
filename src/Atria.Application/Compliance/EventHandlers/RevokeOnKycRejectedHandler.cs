using Atria.Application.Abstractions;
using Atria.Domain.Kyc.Events;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Compliance.EventHandlers;

/// <summary>
/// On KYC rejection, revokes the investor's attestations and removes their wallet
/// from the permissioned allowlist, then marks the profile revoked. Exactly-once via
/// <see cref="IProcessedEventStore"/> so the revocation/allowlist effects happen once.
/// </summary>
public sealed class RevokeOnKycRejectedHandler : IDomainEventHandler<KycRejectedEvent>
{
    private readonly IComplianceRepository _profiles;
    private readonly ITesseraComplianceService _tessera;
    private readonly IProcessedEventStore _processed;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<RevokeOnKycRejectedHandler> _logger;

    public RevokeOnKycRejectedHandler(
        IComplianceRepository profiles,
        ITesseraComplianceService tessera,
        IProcessedEventStore processed,
        IUnitOfWork uow,
        ILogger<RevokeOnKycRejectedHandler> logger)
    {
        _profiles = profiles;
        _tessera = tessera;
        _processed = processed;
        _uow = uow;
        _logger = logger;
    }

    public async Task HandleAsync(KycRejectedEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processed.IsProcessedAsync(key, ct))
            return;

        var investorId = domainEvent.UserId;

        var profile = await _profiles.GetByInvestorAsync(investorId, ct);
        if (profile is null)
        {
            // Nothing was ever issued for this investor — no compliance state to revoke.
            _logger.LogInformation(
                "No compliance profile for investor {InvestorId}; nothing to revoke.",
                investorId);
            await _processed.MarkProcessedAsync(key, ct);
            return;
        }

        // Revocation already drops the wallet from every configured network's allowlist; removing it
        // again here would just queue the same operation twice.
        await _tessera.RevokeAttestationsAsync(investorId, domainEvent.Reason, ct);

        if (string.IsNullOrWhiteSpace(profile.WalletAddress))
            _logger.LogWarning(
                "No wallet address for investor {InvestorId}; nothing to remove from the allowlist.",
                investorId);

        // Domain revoke also flips the allowlist flag and raises AttestationsRevokedEvent.
        profile.Revoke(domainEvent.Reason);
        _profiles.Update(profile);

        await _uow.SaveChangesAsync(ct);
        await _processed.MarkProcessedAsync(key, ct);
    }
}
