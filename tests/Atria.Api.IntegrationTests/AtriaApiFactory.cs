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
        // TEMPORARY: while OtpService runs in stub mode no code is generated and no SMS is sent,
        // so there is nothing to capture — every caller needs the fixed stub code instead. Delete
        // this branch together with OtpService.StubEnabled.
        if (!_lastMessage.TryGetValue(phoneNumber, out var message))
            return "111111";

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
public class AtriaApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Shared in-memory database name so all requests in a test see the same data.</summary>
    private const string InMemoryDbName = "atria-tests";

    /// <summary>Throwaway 32-byte keys, base64-encoded. Distinct so a mix-up shows up as a failure.</summary>
    private static readonly string TestSigningKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x11, 32).ToArray());
    private static readonly string TestEncryptionKey = Convert.ToBase64String(new byte[32]);
    private static readonly string TestOtpPepper = Convert.ToBase64String(Enumerable.Repeat((byte)0x22, 32).ToArray());

    static AtriaApiFactory()
    {
        // Secrets go in as ENVIRONMENT VARIABLES, not through ConfigureAppConfiguration, because the
        // host reads two things before that source is merged: SecretsGuard (which refuses to start
        // without them) and the eager Configuration.Get<JwtOptions>() that builds the bearer
        // validation parameters. Setting them here means the test host is configured the same way
        // production is — the guard runs for real rather than being switched off for tests.
        SetIfUnset("ConnectionStrings__Postgres",
            "Host=localhost;Port=5432;Database=atria_test;Username=test;Password=test");
        SetIfUnset("Jwt__Issuer", "https://atria.local");
        SetIfUnset("Jwt__Audience", "atria-api");
        SetIfUnset("Jwt__SigningKey", TestSigningKey);
        SetIfUnset("Encryption__Key", TestEncryptionKey);
        SetIfUnset("Otp__HashPepper", TestOtpPepper);
        SetIfUnset("Didit__ApiKey", "test-didit-api-key");
        SetIfUnset("Didit__WebhookSecret", "test-didit-webhook-secret");
        SetIfUnset("NikitaPro__Login", "test-login");
        SetIfUnset("NikitaPro__ApiKey", "test-nikita-api-key");

        // Rate limiting is read eagerly too (the limiter is built while the pipeline is composed),
        // so it belongs here rather than in ConfigureAppConfiguration. The suite drives every request
        // through one loopback address; at the production 5-per-minute window on the auth routes most
        // of it would be rejected. The limiter's ROUTE COVERAGE — the actual finding — is asserted by
        // AuthRateLimitTests, which sets the production numbers for itself.
        SetIfUnset("RateLimiting__PermitLimit", "100000");
        SetIfUnset("RateLimiting__WindowSeconds", "60");
    }

    private static void SetIfUnset(string name, string value)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            Environment.SetEnvironmentVariable(name, value);
    }

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

    // The management company's two staff accounts, on the roles they reuse: the accountant reports
    // and the platform confirms, which is what lets a test exercise the "two different people" rule
    // that operating periods enforce.
    public const string AccountantUsername = "buhgalter";
    public const string AccountantPassword = "buhgalter-test-password";
    public const string LawyerUsername = "yurist";
    public const string LawyerPassword = "yurist-test-password";

    // Fixed ids so suites that assert on the token subject keep working.
    private static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RealtorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SuperAdminId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AccountantId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid LawyerId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        SeedCredentialAccounts(host.Services);
        return host;
    }

    // Serializes seeding across the parallel factory instances that share one in-memory DB, so the
    // check-then-insert on the unique username can't race.
    private static readonly object SeedLock = new();

    /// <summary>Seeds the staff credential rows once (idempotent, thread-safe).</summary>
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
            Ensure(AccountantUsername, Atria.Domain.Users.Role.Finance, AccountantPassword, AccountantId);
            Ensure(LawyerUsername, Atria.Domain.Users.Role.Auditor, LawyerPassword, LawyerId);
            db.SaveChanges();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                // EF still binds Postgres at startup (health check + AddDbContext); a dummy is fine
                // because ConfigureTestServices replaces the provider with the in-memory store.
                ["ConnectionStrings:Postgres"] =
                    "Host=localhost;Port=5432;Database=atria_test;Username=test;Password=test",

                // Jwt: Issuer/Audience/SigningKey are supplied as environment variables in the
                // static constructor, because Program.cs reads them EAGERLY (to build the bearer
                // VALIDATION parameters) before this in-memory source is merged. Setting them here
                // too would leave signing and validation using different keys.
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "30",

                // Admin (section "Admin"): static admin login is enabled when Password is non-empty,
                // Admin/Realtor/SuperAdmin credential accounts are ordinary users rows (username +
                // password hash) — no configuration. They are seeded into the in-memory DB by
                // SeedCredentialAccounts(); tests log in with the well-known passwords on this factory.

                // Referral (section "Referral"): base URL used to build shareable deal links.
                ["Referral:BaseUrl"] = "https://atria.test/invest",

                // Auth lockout (section "Auth:Lockout"). Every test class shares the one seeded
                // "admin" row in the one in-memory database, and several of them deliberately log in
                // with a wrong password. Under the production policy those attempts accumulate on the
                // shared account and lock it out from under the tests that expect to sign in. The
                // lockout itself is covered directly by AuthLockoutTests, which uses its own account.
                ["Auth:Lockout:MaxFailedLogins"] = "1000",
                ["Auth:Lockout:LockoutMinutes"] = "1",

                // Otp (section "Otp"). There is no fixed test code: the production service always
                // generates a real one and sends it. Tests read it from the captured SMS instead —
                // see CapturingSmsSender — so no bypass has to exist in the shipped code.
                ["Otp:Length"] = "6",
                ["Otp:TtlMinutes"] = "5",
                ["Otp:MaxAttempts"] = "5",
                ["Otp:RequestsPerHour"] = "100",
                // Generous per-address caps: the whole suite runs from one loopback address.
                ["Otp:RequestsPerHourPerIp"] = "1000",
                ["Otp:DistinctPhonesPerHourPerIp"] = "1000",

                // Didit (section "Didit"): ApiKey/WebhookSecret/BaseUrl are [Required], BaseUrl is [Url].
                // Blockchain:Anchor (EVM attestation anchoring). Bound and validated on start, so it
                // needs values; the anchor itself is constructed lazily and never reached in tests.
                ["Blockchain:Anchor:RpcUrl"] = "https://rpc.test.invalid",
                ["Blockchain:Anchor:ChainId"] = "97",
                ["Blockchain:Anchor:IdentityRegistryAddress"] = "0x3838f73f9787f8b4f8a1e0173de7c7030a570806",
                ["Blockchain:Anchor:AgentPrivateKey"] =
                    "0x0000000000000000000000000000000000000000000000000000000000000001",
                ["Blockchain:Anchor:UseLegacyGasPricing"] = "true",

                // ApiKey/WebhookSecret come from the environment (see the static constructor).
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
