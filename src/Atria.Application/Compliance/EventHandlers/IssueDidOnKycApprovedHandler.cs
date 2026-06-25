using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Domain.Compliance;
using Atria.Domain.Kyc.Events;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Compliance.EventHandlers;

/// <summary>
/// On KYC approval, provisions the investor's off-chain compliance record: creates a
/// <see cref="ComplianceProfile"/> if none exists, issues a DID + attestations
/// (kyc_verified, phone_verified, resident) via the Tessera facade, and stores them.
/// Exactly-once via <see cref="IProcessedEventStore"/> so the DID is issued at most once.
/// </summary>
public sealed class IssueDidOnKycApprovedHandler : IDomainEventHandler<KycApprovedEvent>
{
    private readonly IComplianceRepository _profiles;
    private readonly ITesseraComplianceService _tessera;
    private readonly IProcessedEventStore _processed;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<IssueDidOnKycApprovedHandler> _logger;

    public IssueDidOnKycApprovedHandler(
        IComplianceRepository profiles,
        ITesseraComplianceService tessera,
        IProcessedEventStore processed,
        IUnitOfWork uow,
        ILogger<IssueDidOnKycApprovedHandler> logger)
    {
        _profiles = profiles;
        _tessera = tessera;
        _processed = processed;
        _uow = uow;
        _logger = logger;
    }

    public async Task HandleAsync(KycApprovedEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processed.IsProcessedAsync(key, ct))
            return;

        // The investor is identified by the user behind the KYC profile.
        var investorId = domainEvent.UserId;

        var profile = await _profiles.GetByInvestorAsync(investorId, ct);
        if (profile is null)
        {
            profile = ComplianceProfile.Create(investorId, domainEvent.WalletAddress);
            await _profiles.AddAsync(profile, ct);
        }

        // Wallet may come from the event or a previously stored profile.
        var wallet = domainEvent.WalletAddress ?? profile.WalletAddress;

        var attestations = new[]
        {
            AttestationType.KycVerified,
            AttestationType.PhoneVerified,
            AttestationType.Resident
        };

        if (string.IsNullOrWhiteSpace(wallet))
        {
            // No wallet bound yet: skip on-chain anchoring/allowlist; the DID and
            // attestations are still issued off-chain so the profile is usable.
            _logger.LogWarning(
                "No wallet address for investor {InvestorId}; issuing DID/attestations off-chain only.",
                investorId);
        }

        var result = await _tessera.IssueDidAndAttestationsAsync(
            investorId,
            wallet ?? string.Empty,
            attestations,
            ct);

        profile.SetDid(result.Did);
        profile.SetAttestations(SerializeAttestations(result));
        _profiles.Update(profile);

        await _uow.SaveChangesAsync(ct);
        await _processed.MarkProcessedAsync(key, ct);
    }

    // JSON for the issued attestations payload. Built via System.Text.Json so values
    // containing quotes/backslashes (DID, anchored root) cannot produce malformed JSON.
    private static string SerializeAttestations(AttestationIssueResult result)
    {
        var payload = new
        {
            did = result.Did,
            types = result.IssuedAttestationTypes.Select(t => t.ToString()).ToArray(),
            anchoredRoot = result.AnchoredRoot
        };
        return JsonSerializer.Serialize(payload);
    }
}
