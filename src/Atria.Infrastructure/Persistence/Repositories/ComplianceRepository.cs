using Atria.Application.Abstractions;
using Atria.Domain.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class ComplianceRepository : Repository<ComplianceProfile>, IComplianceRepository
{
    public ComplianceRepository(AtriaDbContext db) : base(db) { }

    public Task<ComplianceProfile?> GetByInvestorAsync(Guid investorId, CancellationToken ct)
        => Set.FirstOrDefaultAsync(c => c.InvestorId == investorId, ct);

    // Read-only: the caller only needs the investor behind the address.
    /// <remarks>
    /// Compared case-insensitively, because the same address reaches us in two different casings and
    /// they must resolve to one investor. An investor's wallet is stored as they submitted it —
    /// MetaMask hands out the EIP-55 mixed-case form — while addresses decoded from chain events are
    /// lowercase hex. An exact comparison silently fails to match every address that came off the
    /// chain, which does not look like a bug: the registry simply reports a holder it cannot tie to
    /// anyone, and the travel-rule check concludes the transfer was not ours to report.
    /// </remarks>
    public Task<ComplianceProfile?> GetByWalletAsync(string walletAddress, CancellationToken ct)
        => Set.AsNoTracking()
            .Where(c => c.WalletAddress != null && c.WalletAddress.ToLower() == walletAddress.ToLower())
            // Ordered so that a pre-existing pair of case-variant rows resolves to the same profile
            // every time rather than to whichever the planner returned first. The real fix is
            // normalising addresses on write; until that migration runs, at least be deterministic.
            .OrderBy(c => c.WalletAddress)
            .FirstOrDefaultAsync(ct);
}
