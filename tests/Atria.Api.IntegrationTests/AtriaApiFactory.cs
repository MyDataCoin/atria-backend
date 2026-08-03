using System.Collections.Generic;
using System.Linq;
using Atria.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Atria.Application.Abstractions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Stands in for the SMS gateway and remembers the last message per phone, so tests can read the
/// verification code the service really produced. The production OTP path is exercised unchanged —
/// a real random code, hashed and stored, single-use — which is the point: no test convenience is
/// allowed to live in the shipped code.
/// </summary>
public sealed class CapturingSmsSender : ISmsSender
{
    private readonly ConcurrentDictionary<string, string> _lastMessage = new();

    public Task SendAsync(string phoneNumber, string message, CancellationToken ct)
    {
        _lastMessage[phoneNumber] = message;
        return Task.CompletedTask;
    }

    /// <summary>The verification code most recently sent to a phone.</summary>
    public string CodeFor(string phoneNumber)
    {
        if (!_lastMessage.TryGetValue(phoneNumber, out var message))
            throw new InvalidOperationException($"No OTP was sent to {phoneNumber}.");

        var match = Regex.Match(message, @"\b(\d{4,10})\b");
        if (!match.Success)
            throw new InvalidOperationException($"No code found in the message sent to {phoneNumber}: {message}");

        return match.Groups[1].Value;
    }
}

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

    /// <summary>
    /// Captures the SMS the API would have sent, so a test can read the OTP the service actually
    /// generated. Shared with the host through DI.
    /// </summary>
    public CapturingSmsSender Sms { get; } = new();

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

                // Otp (section "Otp"). There is no fixed test code: the production service always
                // generates a real one and sends it. Tests read it from the captured SMS instead —
                // see CapturingSmsSender — so no bypass has to exist in the shipped code.
                ["Otp:Length"] = "6",
                ["Otp:TtlMinutes"] = "5",
                ["Otp:MaxAttempts"] = "5",
                ["Otp:RequestsPerHour"] = "100",

                // Didit (section "Didit"): ApiKey/WebhookSecret/BaseUrl are [Required], BaseUrl is [Url].
                // Blockchain:Anchor (EVM attestation anchoring). Bound and validated on start, so it
                // needs values; the anchor itself is constructed lazily and never reached in tests.
                ["Blockchain:Anchor:RpcUrl"] = "https://rpc.test.invalid",
                ["Blockchain:Anchor:ChainId"] = "97",
                ["Blockchain:Anchor:IdentityRegistryAddress"] = "0x3838f73f9787f8b4f8a1e0173de7c7030a570806",
                ["Blockchain:Anchor:AgentPrivateKey"] =
                    "0x0000000000000000000000000000000000000000000000000000000000000001",
                ["Blockchain:Anchor:UseLegacyGasPricing"] = "true",

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

            // Capture outgoing SMS instead of calling a gateway. This is what lets the OTP flow be
            // exercised end to end without a bypass in the production code path.
            services.RemoveAll<ISmsSender>();
            services.AddSingleton<ISmsSender>(Sms);
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
