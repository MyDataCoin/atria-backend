using System.Collections.Generic;
using System.Linq;
using Atria.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Hosts the real <c>Atria.Api</c> pipeline in-process for integration tests. It:
/// <list type="bullet">
///   <item>runs in the "Testing" environment (HTTPS redirection / HSTS off);</item>
///   <item>injects dummy-but-valid configuration so every <c>ValidateOnStart</c> option binds
///         (Postgres connection string, JWT, a real base64 32-byte encryption key, and every
///         provider secret marked <c>[Required]</c>);</item>
///   <item>swaps the Postgres-backed <see cref="AtriaDbContext"/> for an EF Core in-memory store;</item>
///   <item>removes the hosted background workers so they do not run during tests.</item>
/// </list>
/// </summary>
public sealed class AtriaApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Shared in-memory database name so all requests in a test see the same data.</summary>
    private const string InMemoryDbName = "atria-tests";

    // Well-known credential accounts seeded into the in-memory DB (username / password). Login is
    // purely DB-based now, so tests obtain tokens with these.
    public const string AdminUsername = "admin";
    public const string AdminPassword = "admin-test-password";
    public const string RealtorUsername = "realtor";
    public const string RealtorPassword = "realtor-test-password";
    public const string SuperAdminUsername = "superadmin";
    public const string SuperAdminPassword = "superadmin-test-password";

    // Fixed ids so suites that assert on the token subject keep working.
    private static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RealtorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SuperAdminId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        SeedCredentialAccounts(host.Services);
        return host;
    }

    // Serializes seeding across the parallel factory instances that share one in-memory DB, so the
    // check-then-insert on the unique username can't race.
    private static readonly object SeedLock = new();

    /// <summary>Seeds the admin/realtor/super-admin credential rows once (idempotent, thread-safe).</summary>
    private static void SeedCredentialAccounts(IServiceProvider services)
    {
        lock (SeedLock)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<Atria.Application.Abstractions.IPasswordHasher>();

            void Ensure(string username, Atria.Domain.Users.Role role, string password, Guid id)
            {
                if (!db.Users.Any(u => u.Username == username))
                    db.Users.Add(Atria.Domain.Users.User.CreateServiceAccount(username, role, hasher.Hash(password), id));
            }

            Ensure(AdminUsername, Atria.Domain.Users.Role.Admin, AdminPassword, AdminId);
            Ensure(RealtorUsername, Atria.Domain.Users.Role.Realtor, RealtorPassword, RealtorId);
            Ensure(SuperAdminUsername, Atria.Domain.Users.Role.SuperAdmin, SuperAdminPassword, SuperAdminId);
            db.SaveChanges();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // 32 zero bytes -> a valid base64 256-bit AES key for EncryptionOptions.Key.
            var encryptionKey = System.Convert.ToBase64String(new byte[32]);

            var settings = new Dictionary<string, string?>
            {
                // EF still binds Postgres at startup (health check + AddDbContext); a dummy is fine
                // because ConfigureTestServices replaces the provider with the in-memory store.
                ["ConnectionStrings:Postgres"] =
                    "Host=localhost;Port=5432;Database=atria_test;Username=test;Password=test",

                // Jwt (section "Jwt"). NOTE: Program.cs reads these EAGERLY
                // (Configuration.Get<JwtOptions>()) to build the bearer VALIDATION parameters,
                // which happens before this in-memory source is merged — so validation uses
                // appsettings.json's Jwt values. Token SIGNING, by contrast, uses
                // IOptions<JwtOptions> resolved lazily at request time and DOES see these overrides.
                // Keep Issuer/Audience/SigningKey identical to appsettings.json so the signing and
                // validation sides agree and tokens issued in tests validate on protected routes.
                ["Jwt:Issuer"] = "https://atria.local",
                ["Jwt:Audience"] = "atria-api",
                ["Jwt:SigningKey"] = "dev-only-signing-key-not-a-real-secret-change-me-32+bytes",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "30",

                // Admin (section "Admin"): static admin login is enabled when Password is non-empty,
                // Admin/Realtor/SuperAdmin credential accounts are ordinary users rows (username +
                // password hash) — no configuration. They are seeded into the in-memory DB by
                // SeedCredentialAccounts(); tests log in with the well-known passwords on this factory.

                // Referral (section "Referral"): base URL used to build shareable deal links.
                ["Referral:BaseUrl"] = "https://atria.test/invest",

                // Encryption (section "Encryption"): base64 of exactly 32 bytes.
                ["Encryption:Key"] = encryptionKey,

                // Otp (section "Otp"). DevFixedCode makes the OTP a known value and skips SMS,
                // so the phone auth flow can be exercised end-to-end in tests.
                ["Otp:Length"] = "6",
                ["Otp:TtlMinutes"] = "5",
                ["Otp:MaxAttempts"] = "5",
                ["Otp:RequestsPerHour"] = "100",
                ["Otp:DevFixedCode"] = "333333",

                // Didit (section "Didit"): ApiKey/WebhookSecret/BaseUrl are [Required], BaseUrl is [Url].
                ["Didit:ApiKey"] = "test-didit-api-key",
                ["Didit:WebhookSecret"] = "test-didit-webhook-secret",
                ["Didit:BaseUrl"] = "https://verification.didit.test",
                ["Didit:WebhookToleranceSeconds"] = "300",

                // Stripe (section "Stripe").
                ["Stripe:ApiKey"] = "sk_test_dummy",
                ["Stripe:WebhookSecret"] = "whsec_test_dummy",
                ["Stripe:DefaultCurrency"] = "usd",
                ["Stripe:WebhookToleranceSeconds"] = "300",

                // BankTransfer (section "BankTransfer").
                ["BankTransfer:WebhookSecret"] = "test-bank-webhook-secret",
                ["BankTransfer:BeneficiaryName"] = "Atria Test Ltd",
                ["BankTransfer:Iban"] = "DE00000000000000000000",
                ["BankTransfer:Bic"] = "ATRIATEST",
                ["BankTransfer:BankName"] = "Test Bank",
                ["BankTransfer:WebhookToleranceSeconds"] = "300",

                // NikitaPro (section "NikitaPro"): BaseUrl is [Url].
                ["NikitaPro:Login"] = "test-login",
                ["NikitaPro:Sender"] = "ATRIA",
                ["NikitaPro:ApiKey"] = "test-nikita-api-key",
                ["NikitaPro:BaseUrl"] = "https://smspro.nikita.test/api/",

                // S3 (section "S3").
                ["S3:BucketName"] = "atria-documents-test",
                ["S3:Region"] = "eu-central-1",

                // Tessera (section "Tessera").
                ["Tessera:PolicyId"] = "atria-test-policy",
                ["Tessera:IssuerDid"] = "did:atria:test-issuer",

                // Blockchain (section "Blockchain"): SignerUrl is [Url].
                ["Blockchain:SignerUrl"] = "https://signer.atria.test",
                ["Blockchain:ChainId"] = "97",
                ["Blockchain:TokenContractAddress"] = "0x0000000000000000000000000000000000000000",
                ["Blockchain:AnchorNetwork"] = "solana-devnet"
            };

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureTestServices(services =>
        {
            // Drop EVERY Npgsql-backed registration. AddDbContext registers not just
            // DbContextOptions<AtriaDbContext> + AtriaDbContext, but (EF Core 9) an
            // IDbContextOptionsConfiguration<AtriaDbContext> that pins the Npgsql provider.
            // Leaving any of these in place makes EF see two providers and throw at first use.
            RemoveEfCoreRegistrationsFor(services);

            services.AddDbContext<AtriaDbContext>(options =>
                options.UseInMemoryDatabase(InMemoryDbName));

            // Remove the hosted background workers so they do not poll the (fake) DB during tests.
            RemoveHostedServices(services);
        });
    }

    /// <summary>
    /// Removes the production (Npgsql) EF Core registrations for <see cref="AtriaDbContext"/>:
    /// the context itself, its <see cref="DbContextOptions"/> (generic + non-generic), and any
    /// EF Core 9 <c>IDbContextOptionsConfiguration&lt;AtriaDbContext&gt;</c> descriptor that pins
    /// the Npgsql provider. Matched by type name so we do not need a direct reference to the
    /// internal options-configuration interface.
    /// </summary>
    private static void RemoveEfCoreRegistrationsFor(IServiceCollection services)
    {
        var toRemove = services.Where(d =>
                d.ServiceType == typeof(AtriaDbContext)
                || d.ServiceType == typeof(DbContextOptions<AtriaDbContext>)
                || d.ServiceType == typeof(DbContextOptions)
                || IsDbContextOptionsConfigurationFor(d.ServiceType, typeof(AtriaDbContext)))
            .ToList();

        foreach (var descriptor in toRemove)
        {
            services.Remove(descriptor);
        }
    }

    private static bool IsDbContextOptionsConfigurationFor(System.Type serviceType, System.Type contextType)
        => serviceType.IsGenericType
            && serviceType.Name.StartsWith("IDbContextOptionsConfiguration", System.StringComparison.Ordinal)
            && serviceType.GetGenericArguments() is [var arg] && arg == contextType;

    private static void RemoveHostedServices(IServiceCollection services)
    {
        var hosted = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();

        foreach (var descriptor in hosted)
        {
            services.Remove(descriptor);
        }
    }
}
