using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueHolderSnapshotCut : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_holder_snapshots_PropertyId_SnapshotAtUtc",
                table: "holder_snapshots");

            migrationBuilder.CreateIndex(
                name: "IX_holder_snapshots_PropertyId_SnapshotAtUtc_Purpose",
                table: "holder_snapshots",
                columns: new[] { "PropertyId", "SnapshotAtUtc", "Purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_holder_snapshots_PropertyId_SnapshotAtUtc_Purpose",
                table: "holder_snapshots");

            migrationBuilder.CreateIndex(
                name: "IX_holder_snapshots_PropertyId_SnapshotAtUtc",
                table: "holder_snapshots",
                columns: new[] { "PropertyId", "SnapshotAtUtc" });
        }
    }
}
