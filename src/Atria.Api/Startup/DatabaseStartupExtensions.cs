using Atria.Infrastructure.Persistence;
using Atria.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Atria.Api.Startup;

/// <summary>
/// Optional startup database bootstrap for dev/compose: apply EF migrations (with retry, since
/// the DB container may still be coming up) and seed demo data. Both steps are gated by config
/// and OFF by default — never auto-migrate/seed a production database you do not control.
/// </summary>
public static class DatabaseStartupExtensions
{
    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        var migrate = app.Configuration.GetValue<bool>("Database:MigrateOnStartup");
        var seed = app.Configuration.GetValue<bool>("Database:SeedOnStartup");
        if (!migrate && !seed)
            return;

        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<Program>>();

        if (migrate)
            await ApplyMigrationsAsync(sp.GetRequiredService<AtriaDbContext>(), logger);

        if (seed)
            await DataSeeder.SeedAsync(sp.GetRequiredService<AtriaDbContext>(), logger);
    }

    private static async Task ApplyMigrationsAsync(AtriaDbContext db, ILogger logger)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
                logger.LogInformation(
                    "Applying {Count} pending EF migration(s): {Names}",
                    pending.Count, pending.Count == 0 ? "(none)" : string.Join(", ", pending));

                await db.Database.MigrateAsync();
                logger.LogInformation("Database migrations are up to date.");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                // The DB container may still be starting; back off and retry.
                logger.LogWarning(ex,
                    "Migration attempt {Attempt}/{Max} failed (DB not ready?); retrying in 3s...",
                    attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
