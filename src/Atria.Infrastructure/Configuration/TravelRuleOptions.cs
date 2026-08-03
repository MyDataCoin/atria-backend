using System.ComponentModel.DataAnnotations;
using Atria.Application.TravelRule;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Travel-rule settings (FATF R.16): what must be reported, and who we are when reporting it.
/// </summary>
/// <remarks>
/// There is deliberately no counterparty endpoint here yet. Which service provider runs secondary
/// trading is an open decision, and inventing a URL for it would produce a gateway that looks
/// configured and delivers nothing.
/// </remarks>
public sealed class TravelRuleOptions : ITravelRuleSettings
{
    public const string SectionName = "TravelRule";

    /// <summary>
    /// Transfer value at or above which information must travel with the transfer. Set by the
    /// regulator, so it is configuration rather than a constant.
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal ThresholdAmount { get; init; } = 100_000m;

    /// <summary>Currency the threshold is expressed in.</summary>
    [Required]
    public string ThresholdCurrency { get; init; } = "KGS";

    /// <summary>Our own name as the reporting service provider.</summary>
    [Required]
    public string OriginatingVaspName { get; init; } = "ATRIA";

    /// <summary>Our identifier with counterparties (LEI or registry number), when we have one.</summary>
    public string? OriginatingVaspId { get; init; }
}
