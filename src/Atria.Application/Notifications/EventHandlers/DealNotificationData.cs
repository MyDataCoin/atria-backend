using System.Globalization;
using Atria.Application.Abstractions;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>
/// Shared builder for the substitution data of realtor deal notifications: resolves the property
/// name (so the body reads the object, not a GUID) and formats the deal reference + commission.
/// </summary>
internal static class DealNotificationData
{
    public static async Task<IReadOnlyDictionary<string, string>> BuildAsync(
        IPropertyRepository properties, Guid dealId, Guid propertyId, decimal commissionPercent,
        CancellationToken ct)
    {
        var property = await properties.GetByIdAsync(propertyId, ct);
        return new Dictionary<string, string>
        {
            ["dealId"] = dealId.ToString(),
            ["propertyId"] = propertyId.ToString(),
            ["propertyName"] = property?.Name ?? string.Empty,
            ["commissionPercent"] = commissionPercent.ToString("0.##", CultureInfo.InvariantCulture)
        };
    }
}
