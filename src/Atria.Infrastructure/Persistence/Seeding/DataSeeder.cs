using Atria.Domain.Common;
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
        //
        // The denomination follows the rule the platform is sized by: a token costs no more than one
        // percent of the minimum entry. At a 10 000 KGS minimum that is a 100 KGS token and a
        // 100-token floor, and the issue is the object's value divided by that price. Sized any
        // coarser and an investor entering a round sum lands between two tokens — which is the whole
        // reason the counts are whole (see TokenAmount).
        const decimal tokenPrice = 100m;
        const long minPurchase = 100;

        var properties = new[]
        {
            Property.Create(
                "Bishkek Central Residence",
                "Premium residential complex in the heart of Bishkek (Erkindik Blvd).",
                "Erkindik Blvd 12, Bishkek", 50_000_000m, tokenPrice, 500_000, Money.Currency,
                minPurchaseTokens: minPurchase),
            Property.Create(
                "Issyk-Kul Resort Villas",
                "Beachfront resort villas on the northern shore of Issyk-Kul.",
                "Cholpon-Ata, Issyk-Kul Region", 120_000_000m, tokenPrice, 1_200_000, Money.Currency,
                minPurchaseTokens: minPurchase),
            Property.Create(
                "Osh Commercial Plaza",
                "Mixed-use commercial plaza in central Osh.",
                "Kurmanjan Datka St 45, Osh", 80_000_000m, tokenPrice, 800_000, Money.Currency,
                minPurchaseTokens: minPurchase),
            Property.Create(
                "Ala-Too Business Center",
                "Class-A office tower near Ala-Too Square, Bishkek.",
                "Chuy Ave 136, Bishkek", 200_000_000m, tokenPrice, 2_000_000, Money.Currency,
                minPurchaseTokens: minPurchase),
            Property.Create(
                "Karakol Mountain Lodge",
                "Boutique lodge serving the Karakol ski resort.",
                "Karakol, Issyk-Kul Region", 35_000_000m, tokenPrice, 350_000, Money.Currency,
                minPurchaseTokens: minPurchase),
        };

        // Demo objects are meant to be live on the public site, so publish them (Draft -> Open).
        foreach (var property in properties)
            property.Publish();

        await db.Properties.AddRangeAsync(properties, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded {Count} demo tokenization properties.", properties.Length);
    }
}
