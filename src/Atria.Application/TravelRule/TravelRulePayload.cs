using System.Text.Encodings.Web;
using System.Text.Json;
using Atria.Domain.TravelRule;

namespace Atria.Application.TravelRule;

/// <summary>
/// Builds the payload that travels with a transfer, in the IVMS101 shape every travel-rule network
/// speaks underneath its own transport.
/// </summary>
/// <remarks>
/// <para>
/// IVMS101 is the one part of this obligation that does not depend on which counterparty is chosen:
/// TRP, TRISA and the commercial networks all carry the same person and account structures inside
/// different envelopes. Building it now means picking a network later is a transport change, not a
/// rewrite of what we disclose.
/// </para>
/// <para>
/// Only fields we have actually verified are emitted. An empty document number is left out rather
/// than sent as an empty string: the counterparty's own checks distinguish "not provided" from
/// "provided as blank", and the second is a statement we cannot support.
/// </para>
/// </remarks>
public static class TravelRulePayload
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        // Names and addresses are Cyrillic more often than not; escaping them to \uXXXX would leave
        // the counterparty's compliance officer reading escape sequences.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Renders one message as the payload to hand over.
    /// </summary>
    /// <param name="message">The assembled disclosure.</param>
    /// <param name="originatingVasp">Our own name as the reporting service provider.</param>
    /// <param name="originatingVaspId">Our identifier with the counterparty (LEI or registry number).</param>
    public static string Build(TravelRuleMessage message, string originatingVasp, string? originatingVaspId)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = new
        {
            originator = new
            {
                originatorPersons = new[]
                {
                    new
                    {
                        naturalPerson = new
                        {
                            name = new
                            {
                                nameIdentifier = new[]
                                {
                                    new { primaryIdentifier = message.OriginatorName, nameIdentifierType = "LEGL" }
                                }
                            },
                            nationalIdentification = string.IsNullOrWhiteSpace(message.OriginatorDocumentNumber)
                                ? null
                                : new
                                {
                                    nationalIdentifier = message.OriginatorDocumentNumber,
                                    nationalIdentifierType = "RAID"
                                },
                            countryOfResidence = message.OriginatorNationality
                        }
                    }
                },
                accountNumber = new[] { message.OriginatorAddress }
            },
            beneficiary = new
            {
                // Absent until the counterparty tells us who is receiving. We report the address we
                // can see and do not guess at the person behind it.
                beneficiaryPersons = string.IsNullOrWhiteSpace(message.BeneficiaryName)
                    ? null
                    : new[]
                    {
                        new
                        {
                            naturalPerson = new
                            {
                                name = new
                                {
                                    nameIdentifier = new[]
                                    {
                                        new { primaryIdentifier = message.BeneficiaryName, nameIdentifierType = "LEGL" }
                                    }
                                }
                            }
                        }
                    },
                accountNumber = new[] { message.BeneficiaryAddress }
            },
            originatingVasp = new
            {
                name = originatingVasp,
                identifier = originatingVaspId
            },
            beneficiaryVasp = new { name = message.CounterpartyVasp },
            transfer = new
            {
                assetType = "security-token",
                propertyId = message.PropertyId,
                // Indivisible shares, so the count is the amount as written — no decimals to lose.
                amount = message.TokenCount,
                value = message.Amount,
                currency = message.Currency,
                transactionHash = message.TransactionHash,
                direction = message.Direction.ToString()
            }
        };

        return JsonSerializer.Serialize(payload, Options);
    }
}
