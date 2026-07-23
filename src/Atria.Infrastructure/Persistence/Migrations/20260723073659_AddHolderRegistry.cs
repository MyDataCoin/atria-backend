using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHolderRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "holder_positions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenCount = table.Column<long>(type: "bigint", nullable: false),
                    InvestorId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsAllowlisted = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_holder_positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "holder_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    BlockNumber = table.Column<long>(type: "bigint", nullable: true),
                    TotalTokens = table.Column<long>(type: "bigint", nullable: false),
                    AddressCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_holder_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "holder_snapshot_rows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenCount = table.Column<long>(type: "bigint", nullable: false),
                    InvestorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Share = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_holder_snapshot_rows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_holder_snapshot_rows_holder_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "holder_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_holder_positions_InvestorId",
                table: "holder_positions",
                column: "InvestorId");

            migrationBuilder.CreateIndex(
                name: "IX_holder_positions_PropertyId_WalletAddress",
                table: "holder_positions",
                columns: new[] { "PropertyId", "WalletAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_holder_snapshot_rows_SnapshotId",
                table: "holder_snapshot_rows",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_holder_snapshots_PropertyId_SnapshotAtUtc",
                table: "holder_snapshots",
                columns: new[] { "PropertyId", "SnapshotAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "holder_positions");

            migrationBuilder.DropTable(
                name: "holder_snapshot_rows");

            migrationBuilder.DropTable(
                name: "holder_snapshots");
        }
    }
}
