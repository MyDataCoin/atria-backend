using Atria.Application.Abstractions;
using Atria.Domain.Common;
using Atria.Domain.Investments;
using Atria.Domain.TravelRule;
using Microsoft.Extensions.Logging;

namespace Atria.Application.TravelRule;

/// <summary>Settings governing what must be reported and by whom.</summary>
/// <remarks>
/// The threshold is configuration rather than a constant because it is set by the regulator and
/// moves; hard-coding it would make a rule change a code change.
/// </remarks>
public interface ITravelRuleSettings
{
    /// <summary>Transfer value at or above which information must travel with the transfer.</summary>
    decimal ThresholdAmount { get; }

    /// <summary>Currency the threshold is expressed in.</summary>
    string ThresholdCurrency { get; }

    /// <summary>Our own name as the reporting service provider.</summary>
    string OriginatingVaspName { get; }

    /// <summary>Our identifier with counterparties (LEI or registry number), when we have one.</summary>
    string? OriginatingVaspId { get; }
}

/// <summary>Records the disclosure owed on a transfer between service providers.</summary>
public interface ITravelRuleReporter
{
    /// <summary>
    /// Assembles and queues the information owed on one observed transfer, or does nothing when
    /// none is owed. Returns the message when one was created.
    /// </summary>
    Task<TravelRuleMessage?> ReportTransferAsync(
        Property property, string fromAddress, string toAddress, decimal tokenCount,
        string? transactionHash, CancellationToken ct);
}

/// <summary>
/// Turns an observed transfer into the disclosure the travel rule requires (FATF R.16).
/// </summary>
/// <remarks>
/// <para>
/// Called from the chain sync, because that is the only place a transfer between two holders is
/// actually seen: a trade settles on chain with nobody telling the backend, and a rule that only
/// covered transfers the platform itself initiated would cover exactly the transfers that do not
/// need it.
/// </para>
/// <para>
/// Recording never blocks the sync. The transfer has already happened on chain — refusing to record
/// the registry change because a disclosure could not be assembled would leave the register wrong
/// as well as the disclosure missing. What a failure produces instead is a loud log and no message,
/// which the outstanding queue then does not hide.
/// </para>
/// </remarks>
public sealed class TravelRuleReporter : ITravelRuleReporter
{
    private readonly ITravelRuleRepository _messages;
    private readonly IComplianceRepository _profiles;
    private readonly IKycRepository _kyc;
    private readonly ITravelRuleSettings _settings;
    private readonly ILogger<TravelRuleReporter> _logger;

    public TravelRuleReporter(
        ITravelRuleRepository messages,
        IComplianceRepository profiles,
        IKycRepository kyc,
        ITravelRuleSettings settings,
        ILogger<TravelRuleReporter> logger)
    {
        _messages = messages;
        _profiles = profiles;
        _kyc = kyc;
        _settings = settings;
        _logger = logger;
    }

    public async Task<TravelRuleMessage?> ReportTransferAsync(
        Property property, string fromAddress, string toAddress, decimal tokenCount,
        string? transactionHash, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(property);

        var value = tokenCount * property.TokenPrice;
        if (value < _settings.ThresholdAmount)
            return null;

        // The sender must be someone of ours; a transfer between two outside addresses is not ours
        // to report, and we have no verified identity to report with.
        var originatorProfile = await _profiles.GetByWalletAsync(fromAddress, ct);
        if (originatorProfile is null)
            return null;

        // A destination that is also ours is an internal move between two holders we already know.
        // Nothing travels between service providers, so nothing is owed.
        var beneficiaryProfile = await _profiles.GetByWalletAsync(toAddress, ct);
        if (beneficiaryProfile is not null)
            return null;

        if (transactionHash is not null)
        {
            var existing = await _messages.FindByTransferAsync(
                transactionHash, fromAddress, toAddress, ct);
            if (existing is not null)
                return existing;
        }

        var identity = await _kyc.GetByUserIdAsync(originatorProfile.InvestorId, ct);

        try
        {
            var message = TravelRuleMessage.Assemble(
                property.Id, originatorProfile.InvestorId, TravelRuleDirection.Outgoing, tokenCount,
                value, property.Currency, fromAddress, toAddress,
                identity?.FullName ?? string.Empty, identity?.DocumentNumber, identity?.Nationality,
                beneficiaryName: null, counterpartyVasp: null, transactionHash);

            await _messages.AddAsync(message, ct);

            _logger.LogInformation(
                "Travel rule: queued disclosure for {Tokens} share(s) of {Property} leaving {From} "
                + "for {To} (value {Value} {Currency}).",
                tokenCount, property.Id, fromAddress, toAddress, value, property.Currency);

            return message;
        }
        catch (DomainException ex)
        {
            // Almost always a holder with no verified name — someone whose shares moved before their
            // file was complete. Loud, because a transfer that owes a disclosure and has none is a
            // breach that cannot be repaired after the fact.
            _logger.LogError(
                "Travel rule: cannot report transfer of {Tokens} share(s) of {Property} from {From} "
                + "to {To}: {Reason}",
                tokenCount, property.Id, fromAddress, toAddress, ex.Message);
            return null;
        }
    }
}
