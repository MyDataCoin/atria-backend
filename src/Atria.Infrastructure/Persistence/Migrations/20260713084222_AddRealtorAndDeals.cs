using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRealtorAndDeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferralToken",
                table: "investments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "deals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RealtorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ReferralToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MatchedInvestmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deals_RealtorId",
                table: "deals",
                column: "RealtorId");

            migrationBuilder.CreateIndex(
                name: "IX_deals_ReferralToken",
                table: "deals",
                column: "ReferralToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deals_Status_ExpiresAtUtc",
                table: "deals",
                columns: new[] { "Status", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deals");

            migrationBuilder.DropColumn(
                name: "ReferralToken",
                table: "investments");
        }
    }
}
