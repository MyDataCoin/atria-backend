using Atria.Domain.Common;

namespace Atria.Domain.TravelRule;

/// <summary>Which way the transfer goes relative to this platform.</summary>
public enum TravelRuleDirection
{
    /// <summary>Shares leaving us for another service provider — we are the originating side.</summary>
    Outgoing = 0,

    /// <summary>Shares arriving from another service provider — we are the beneficiary side.</summary>
    Incoming = 1
}

/// <summary>Where a travel-rule message stands.</summary>
public enum TravelRuleStatus
{
    /// <summary>Assembled and waiting to be handed to the counterparty.</summary>
    Pending = 0,

    /// <summary>Delivered to the counterparty.</summary>
    Sent = 1,

    /// <summary>The counterparty confirmed receipt.</summary>
    Acknowledged = 2,

    /// <summary>The counterparty refused the transfer on the data provided.</summary>
    Rejected = 3,

    /// <summary>Delivery failed. Still owed — the reason says what to fix.</summary>
    Failed = 4
}

/// <summary>
/// The originator/beneficiary information owed to the counterparty service provider on a transfer
/// of shares between providers (FATF Recommendation 16, the "travel rule").
/// </summary>
/// <remarks>
/// <para>
/// This record holds personal data by design — that is what the rule requires to travel with a
/// transfer. Three consequences run through the whole type. It never goes on chain, in any form: the
/// chain carries addresses and nothing else. It is assembled from the KYC file at the moment of the
/// transfer and frozen, because what was disclosed has to be reproducible afterwards even if the
/// person's file has since changed. And it is stored separately from the transfer itself, so a
/// screen showing transfers is not a screen showing everyone's documents.
/// </para>
/// <para>
/// The message is recorded whether or not there is anywhere to send it yet. Which service provider
/// runs secondary trading is an open question, so delivery is a separate step performed by a
/// gateway — but a transfer that happened without its information being assembled is a breach that
/// cannot be repaired later, whereas a message sitting in <see cref="TravelRuleStatus.Pending"/> is
/// a queue somebody can work through.
/// </para>
/// </remarks>
public sealed class TravelRuleMessage : AggregateRoot
{
    /// <summary>The issue whose shares moved.</summary>
    public Guid PropertyId { get; private set; }

    public TravelRuleDirection Direction { get; private set; }
    public TravelRuleStatus Status { get; private set; }

    /// <summary>The investor on our side of the transfer.</summary>
    public Guid InvestorId { get; private set; }

    /// <summary>Shares transferred.</summary>
    public decimal TokenCount { get; private set; }

    /// <summary>Value of the transfer, used to judge it against the threshold.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;

    /// <summary>The address the shares left.</summary>
    public string OriginatorAddress { get; private set; } = null!;

    /// <summary>The address the shares arrived at.</summary>
    public string BeneficiaryAddress { get; private set; } = null!;

    /// <summary>
    /// The counterparty service provider, as far as we can name it. Null when the receiving address
    /// belongs to no provider we know — an unhosted wallet, or one we have not identified.
    /// </summary>
    public string? CounterpartyVasp { get; private set; }

    /// <summary>Full name of the person the shares came from, as verified.</summary>
    public string OriginatorName { get; private set; } = null!;

    /// <summary>Identity document number of the originator, as verified.</summary>
    public string? OriginatorDocumentNumber { get; private set; }

    /// <summary>Nationality of the originator, as verified.</summary>
    public string? OriginatorNationality { get; private set; }

    /// <summary>
    /// Full name of the person on the other side, when the counterparty told us. Null on an outgoing
    /// transfer we have not had an answer to yet — we do not invent the other side.
    /// </summary>
    public string? BeneficiaryName { get; private set; }

    /// <summary>The on-chain transaction the transfer was, once there is one.</summary>
    public string? TransactionHash { get; private set; }

    /// <summary>The canonical IVMS101 payload as it was handed over, frozen once sent.</summary>
    public string? Payload { get; private set; }

    /// <summary>When the counterparty took delivery.</summary>
    public DateTime? SentAtUtc { get; private set; }

    /// <summary>Reference the counterparty answered with — their message id.</summary>
    public string? CounterpartyReference { get; private set; }

    /// <summary>Why the counterparty refused, or why delivery failed.</summary>
    public string? FailureReason { get; private set; }

    private TravelRuleMessage() { }

    /// <summary>
    /// Assembles the information owed on a transfer, from the identity data verified at the time.
    /// </summary>
    public static TravelRuleMessage Assemble(
        Guid propertyId, Guid investorId, TravelRuleDirection direction, decimal tokenCount,
        decimal amount, string currency, string originatorAddress, string beneficiaryAddress,
        string originatorName, string? originatorDocumentNumber, string? originatorNationality,
        string? beneficiaryName, string? counterpartyVasp, string? transactionHash)
    {
        if (propertyId == Guid.Empty)
            throw new DomainException("PropertyId is required.");
        if (investorId == Guid.Empty)
            throw new DomainException("InvestorId is required.");
        if (tokenCount <= 0)
            throw new DomainException("Transferred share count must be positive.");
        if (amount <= 0)
            throw new DomainException("Transfer value must be positive.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency is required.");
        if (string.IsNullOrWhiteSpace(originatorAddress))
            throw new DomainException("Originator address is required.");
        if (string.IsNullOrWhiteSpace(beneficiaryAddress))
            throw new DomainException("Beneficiary address is required.");

        // Without a verified name there is nothing to send, and sending a transfer anyway is the
        // breach the rule exists to prevent. Refusing here forces the identity file to be completed
        // before the shares move, which is the only order that works.
        if (string.IsNullOrWhiteSpace(originatorName))
            throw new DomainException(
                "The originator has no verified name; the transfer cannot be reported.");

        return new TravelRuleMessage
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            InvestorId = investorId,
            Direction = direction,
            Status = TravelRuleStatus.Pending,
            TokenCount = tokenCount,
            Amount = amount,
            Currency = currency,
            OriginatorAddress = originatorAddress,
            BeneficiaryAddress = beneficiaryAddress,
            OriginatorName = originatorName,
            OriginatorDocumentNumber = originatorDocumentNumber,
            OriginatorNationality = originatorNationality,
            BeneficiaryName = beneficiaryName,
            CounterpartyVasp = counterpartyVasp,
            TransactionHash = transactionHash
        };
    }

    /// <summary>
    /// Records that the information was handed to the counterparty, with the payload exactly as it
    /// went.
    /// </summary>
    /// <remarks>
    /// The payload is frozen here rather than rebuilt on demand. What was disclosed has to be
    /// answerable years later, and a payload regenerated from today's identity file answers a
    /// different question than the one asked.
    /// </remarks>
    public void MarkSent(string payload, string? counterpartyReference, DateTime nowUtc)
    {
        if (Status is TravelRuleStatus.Sent or TravelRuleStatus.Acknowledged)
            throw new DomainException("This message has already been delivered.");
        if (string.IsNullOrWhiteSpace(payload))
            throw new DomainException("The delivered payload is required.");

        Status = TravelRuleStatus.Sent;
        Payload = payload;
        CounterpartyReference = counterpartyReference;
        SentAtUtc = nowUtc;
        FailureReason = null;
    }

    /// <summary>Records the counterparty's confirmation of receipt.</summary>
    public void Acknowledge(string? counterpartyReference)
    {
        if (Status != TravelRuleStatus.Sent)
            throw new DomainException("Only a delivered message can be acknowledged.");

        Status = TravelRuleStatus.Acknowledged;
        if (!string.IsNullOrWhiteSpace(counterpartyReference))
            CounterpartyReference = counterpartyReference;
    }

    /// <summary>Records that the counterparty refused the transfer on the data provided.</summary>
    public void Reject(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A rejection reason is required.");
        if (Status == TravelRuleStatus.Acknowledged)
            throw new DomainException("An acknowledged message cannot be rejected.");

        Status = TravelRuleStatus.Rejected;
        FailureReason = reason;
    }

    /// <summary>
    /// Records that delivery failed. The message stays owed: an obligation that could not be
    /// delivered is not an obligation discharged.
    /// </summary>
    public void MarkFailed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A failure reason is required.");
        if (Status is TravelRuleStatus.Sent or TravelRuleStatus.Acknowledged)
            throw new DomainException("A delivered message cannot be marked failed.");

        Status = TravelRuleStatus.Failed;
        FailureReason = reason;
    }

    /// <summary>Fills in the beneficiary once the counterparty has told us who they are.</summary>
    public void SetBeneficiary(string beneficiaryName, string? counterpartyVasp)
    {
        if (string.IsNullOrWhiteSpace(beneficiaryName))
            throw new DomainException("Beneficiary name is required.");

        BeneficiaryName = beneficiaryName;
        if (!string.IsNullOrWhiteSpace(counterpartyVasp))
            CounterpartyVasp = counterpartyVasp;
    }
}
