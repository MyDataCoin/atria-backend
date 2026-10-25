namespace Atria.Domain.Investments;

/// <summary>
/// How precisely the developer stated <see cref="Property.PlannedCompletionDate"/>. The date column
/// alone cannot say it: "4 квартал 2027" has to be stored as SOME day, and printing that day back
/// as "31.12.2027" promises investors a precision the developer never gave.
/// </summary>
/// <remarks>
/// Persisted as an int; the wire form is the lowercase name. <see cref="Day"/> is the default, which
/// is what every pre-existing issue — entered as an exact date — reads as.
/// </remarks>
public enum CompletionDatePrecision
{
    /// <summary>An exact date.</summary>
    Day = 0,

    /// <summary>A month: shown as "декабрь 2027".</summary>
    Month = 1,

    /// <summary>A quarter: shown as "IV квартал 2027".</summary>
    Quarter = 2,

    /// <summary>A year: shown as "2027".</summary>
    Year = 3
}
