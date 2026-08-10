using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Refuses to start outside Development when a secret is missing or is a value that has ever been
/// committed to this repository.
/// </summary>
/// <remarks>
/// <para>
/// The compose file guards these with <c>${VAR:?}</c>, but that guard lives in the orchestration
/// file, not in the application. Anything that starts the app another way — <c>dotnet run</c>, a
/// hand-written unit file, a Kubernetes manifest, a staging box someone put together in an
/// afternoon — bypasses it entirely and boots happily on whatever <c>appsettings.json</c> contains.
/// A signing key that lets anyone mint a <c>SuperAdmin</c> token, or a Didit webhook secret that
/// lets an applicant approve their own KYC, is not something to leave to the launcher.
/// </para>
/// <para>
/// Known-bad values are compared by SHA-256 so that fixing the leak does not require re-committing
/// the leaked strings in order to detect them.
/// </para>
/// </remarks>
public static class SecretsGuard
{
    /// <summary>SHA-256 (hex) of values that were once committed and must never authenticate anything again.</summary>
    private static readonly HashSet<string> BurnedValueHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        // "dev-only-signing-key-not-a-real-secret-change-me-32+bytes" — former Jwt:SigningKey default.
        Sha256("dev-only-signing-key-not-a-real-secret-change-me-32+bytes"),
        // "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=" (= "0123456789abcdef0123456789abcdef")
        // — former Encryption:Key default.
        Sha256("MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="),
        Sha256("CHANGE_ME"),
        Sha256("changeme"),
        Sha256("secret"),
        Sha256("password"),
    };

    /// <summary>Configuration keys that must carry a real value before the app may serve traffic.</summary>
    private static readonly (string Key, int MinLength, string What)[] RequiredSecrets =
    {
        ("ConnectionStrings:Postgres", 1, "the database connection string"),
        ("Jwt:SigningKey", 32, "the JWT signing key (base64 of 32 random bytes)"),
        ("Encryption:Key", 44, "the AES-256-GCM key for PII at rest (base64 of 32 bytes)"),
        ("Otp:HashPepper", 44, "the OTP hash pepper (base64 of 32 bytes)"),
        ("Didit:ApiKey", 8, "the Didit API key"),
        ("Didit:WebhookSecret", 16, "the Didit webhook signing secret"),
        // TEMPORARY: SMS delivery is disabled and the Nikita Pro adapter is unregistered, so these
        // credentials are not needed to boot. Restore both lines when SMS comes back.
        // ("NikitaPro:Login", 1, "the Nikita Pro SMS login"),
        // ("NikitaPro:ApiKey", 8, "the Nikita Pro SMS API key"),
    };

    /// <summary>
    /// Validates every required secret. Throws on the first environment that must not boot without
    /// them; in Development the same problems are returned as warnings rather than thrown, so a
    /// developer can run the API with a throwaway configuration.
    /// </summary>
    /// <returns>Human-readable warnings (Development only). Empty when everything checks out.</returns>
    public static IReadOnlyList<string> Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        var problems = new List<string>();

        foreach (var (key, minLength, what) in RequiredSecrets)
        {
            var value = configuration[key];

            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"{key} is not set — supply {what} via the environment (e.g. {EnvVarName(key)}).");
                continue;
            }

            if (BurnedValueHashes.Contains(Sha256(value)))
            {
                problems.Add(
                    $"{key} is a placeholder or a value that was previously committed to this " +
                    $"repository. It is public — generate a new one and set {EnvVarName(key)}.");
                continue;
            }

            if (value.Length < minLength)
            {
                problems.Add(
                    $"{key} is too short ({value.Length} chars, needs at least {minLength}) to be " +
                    $"{what}.");
            }
        }

        if (problems.Count == 0)
            return Array.Empty<string>();

        // Development is allowed to run half-configured; everything else is not. A misconfigured
        // production process that serves traffic is worse than one that refuses to start.
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Refusing to start in the {environment.EnvironmentName} environment: " +
                string.Join(" ", problems));
        }

        return problems;
    }

    /// <summary>"Jwt:SigningKey" -> "Jwt__SigningKey", the form the environment provider reads.</summary>
    private static string EnvVarName(string key) => key.Replace(":", "__", StringComparison.Ordinal);

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
