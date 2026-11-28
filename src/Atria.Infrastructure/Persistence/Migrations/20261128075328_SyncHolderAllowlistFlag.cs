using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncHolderAllowlistFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Register positions projected before their wallet was allowlisted kept the stale «not
            // allowlisted» flag. Bring them in step with the wallet's compliance profile.
            migrationBuilder.Sql("""
                UPDATE holder_positions hp
                SET "IsAllowlisted" = cp."IsAllowlisted"
                FROM compliance_profiles cp
                WHERE lower(cp."WalletAddress") = lower(hp."WalletAddress")
                  AND hp."IsAllowlisted" <> cp."IsAllowlisted";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
