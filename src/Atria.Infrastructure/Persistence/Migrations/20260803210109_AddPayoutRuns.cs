using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayoutRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payout_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    DeclaredAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payout_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayoutRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestorId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenCount = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SettlementReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payout_items_payout_runs_PayoutRunId",
                        column: x => x.PayoutRunId,
                        principalTable: "payout_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payout_items_InvestorId",
                table: "payout_items",
                column: "InvestorId");

            migrationBuilder.CreateIndex(
                name: "IX_payout_items_PayoutRunId",
                table: "payout_items",
                column: "PayoutRunId");

            migrationBuilder.CreateIndex(
                name: "IX_payout_runs_PropertyId",
                table: "payout_runs",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_payout_runs_SnapshotId",
                table: "payout_runs",
                column: "SnapshotId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payout_items");

            migrationBuilder.DropTable(
                name: "payout_runs");
        }
    }
}
