using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atria.Infrastructure.Persistence.Seeding;

/// <summary>
/// Seeds demo tokenization objects (Properties) for development/demo environments.
/// Idempotent: inserts ONLY when the Properties table is empty, so it never duplicates on
/// restart and never touches a populated database. Gated by Database:SeedOnStartup (off in prod).
/// </summary>
public static class DataSeeder
{
    public static async Task SeedAsync(AtriaDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Properties.AnyAsync(ct))
        {
            logger.LogInformation("Property seed skipped: properties already exist.");
            return;
        }

        // Demo Kyrgyzstan real-estate tokenization objects. Currency KGS; full supply available.
        var properties = new[]
        {
            Property.Create(
                "Bishkek Central Residence",
                "Premium residential complex in the heart of Bishkek (Erkindik Blvd).",
                "Erkindik Blvd 12, Bishkek", 50_000_000m, 5_000m, 10_000, "KGS"),
            Property.Create(
                "Issyk-Kul Resort Villas",
                "Beachfront resort villas on the northern shore of Issyk-Kul.",
                "Cholpon-Ata, Issyk-Kul Region", 120_000_000m, 10_000m, 12_000, "KGS"),
            Property.Create(
                "Osh Commercial Plaza",
                "Mixed-use commercial plaza in central Osh.",
                "Kurmanjan Datka St 45, Osh", 80_000_000m, 8_000m, 10_000, "KGS"),
            Property.Create(
                "Ala-Too Business Center",
                "Class-A office tower near Ala-Too Square, Bishkek.",
                "Chuy Ave 136, Bishkek", 200_000_000m, 20_000m, 10_000, "KGS"),
            Property.Create(
                "Karakol Mountain Lodge",
                "Boutique lodge serving the Karakol ski resort.",
                "Karakol, Issyk-Kul Region", 35_000_000m, 3_500m, 10_000, "KGS"),
        };

        await db.Properties.AddRangeAsync(properties, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded {Count} demo tokenization properties.", properties.Length);
    }
}
