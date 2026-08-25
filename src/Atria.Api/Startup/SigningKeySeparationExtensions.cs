using Atria.Domain.Compliance;
using Atria.Domain.Kyc;
using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nethereum.Signer;

namespace Atria.Api.Startup;

/// <summary>
/// Refuses to start when a key the platform signs with belongs to an address some investor holds
/// shares at.
/// </summary>
/// <remarks>
/// <para>
/// The two roles must not meet. A service key signs on the platform's behalf — allowlist writes,
/// mints, collateral attestations; an investor address is the private property of a person the
/// platform never speaks for. One address doing both means the platform is holding a client's key,
/// which is precisely what the custody design exists to avoid, and it makes every on-chain action
/// ambiguous after the fact: nobody can tell whether a transfer was the holder or the operator.
/// </para>
/// <para>
/// This is not hypothetical. A testnet run signed allowlist writes with the wallet of investor
/// +996556018138 — the address was configured as the anchor agent while also sitting in that
/// investor's KYC profile. The write failed only because the address happened not to be an agent on
/// the restriction contract; had it been, the mistake would have gone unnoticed and the platform
/// would have been transacting with a client's key.
/// </para>
/// <para>
/// The check runs at start, against the profiles table, because that is the only place the two sets
/// can be compared — configuration alone cannot know which addresses belong to people. It costs one
/// query on a table with one row per investor.
/// </para>
/// </remarks>
public static class SigningKeySeparationExtensions
{
    public static async Task EnsureSigningKeysAreNotInvestorWalletsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<Program>>();

        var signing = sp.GetRequiredService<IOptions<TokenSigningOptions>>().Value;
        var anchor = sp.GetRequiredService<IOptions<EvmAnchorOptions>>().Value;

        // Named so the failure says which setting to fix, not just which address is wrong.
        var configured = new (string Setting, string? Key)[]
        {
            ($"{EvmAnchorOptions.SectionName}:AgentPrivateKey", anchor.AgentPrivateKey),
            ($"{TokenSigningOptions.SectionName}:MinterPrivateKey", signing.MinterPrivateKey),
            ($"{TokenSigningOptions.SectionName}:OraclePrivateKey", signing.OraclePrivateKey),
            ($"{TokenSigningOptions.SectionName}:CompliancePrivateKey", signing.CompliancePrivateKey),
        };

        var addresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (setting, key) in configured)
        {
            // An absent key is not this check's business: the custody mode legitimately has none,
            // and a malformed one is TokenSigningOptionsValidator's to report.
            if (string.IsNullOrWhiteSpace(key))
                continue;

            string address;
            try
            {
                address = new EthECKey(key.Trim()).GetPublicAddress();
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex, "{Setting} could not be read as a private key; separation not checked for it.",
                    setting);
                continue;
            }

            addresses[address] = setting;
        }

        if (addresses.Count == 0)
            return;

        var db = sp.GetRequiredService<AtriaDbContext>();

        // Read whole and compared in memory: the dictionary above is case-insensitive, and an EVM
        // address is the same address whatever its checksum casing — a case-sensitive comparison
        // would miss exactly the collision that matters. One row per investor, so the read is small.
        //
        // BOTH tables, because an address reaches them at different moments: the wallet is collected
        // after verification and lands in the KYC profile first, and only a handler carries it to the
        // compliance profile. Reading one of them would let a key pass while the address was still in
        // flight — and the address that caused this check to exist sat in the KYC profile a day
        // before it appeared in the compliance one.
        var wallets = await db.Set<ComplianceProfile>()
            .Where(p => p.WalletAddress != null)
            .Select(p => p.WalletAddress!)
            .ToListAsync();

        wallets.AddRange(await db.Set<KycProfile>()
            .Where(p => p.WalletAddress != null)
            .Select(p => p.WalletAddress!)
            .ToListAsync());

        var collisions = FindCollisions(addresses, wallets);

        if (collisions.Count == 0)
        {
            logger.LogInformation(
                "Signing keys checked against investor wallets: {Count} configured, no overlap.",
                addresses.Count);
            return;
        }

        throw new InvalidOperationException(
            "A signing key belongs to an investor's wallet, so the platform would be transacting "
            + "with a client's key: " + string.Join("; ", collisions)
            + ". Configure a dedicated service key for that role.");
    }

    /// <summary>
    /// The addresses that are both signed with and held by an investor, described so the message
    /// names the setting to change. Separated from the startup plumbing so the rule itself can be
    /// tested without booting an application.
    /// </summary>
    public static IReadOnlyList<string> FindCollisions(
        IReadOnlyDictionary<string, string> signingAddressesBySetting,
        IEnumerable<string> investorWallets)
        => investorWallets
            .Where(signingAddressesBySetting.ContainsKey)
            .Select(w => $"{signingAddressesBySetting[w]} signs as {w}, which is an investor's wallet")
            .Distinct()
            .ToList();
}
