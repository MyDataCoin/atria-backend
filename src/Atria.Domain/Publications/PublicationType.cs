namespace Atria.Domain.Publications;

/// <summary>Kind of publication in the news feed. Wire values are lowercase snake_case.</summary>
public enum PublicationType
{
    FinancialReport = 0,
    NewsRelease = 1,
    ValuationAudit = 2,
    GeneralNews = 3
}
