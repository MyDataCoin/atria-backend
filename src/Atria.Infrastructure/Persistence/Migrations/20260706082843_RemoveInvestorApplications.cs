using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveInvestorApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "investor_applications");

            migrationBuilder.DropIndex(
                name: "IX_investments_ApplicationId",
                table: "investments");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "investments");

            migrationBuilder.CreateIndex(
                name: "IX_investments_PropertyId",
                table: "investments",
                column: "PropertyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_investments_PropertyId",
                table: "investments");

            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationId",
                table: "investments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "investor_applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InvestorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investor_applications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_investments_ApplicationId",
                table: "investments",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_investor_applications_InvestorId",
                table: "investor_applications",
                column: "InvestorId");

            migrationBuilder.CreateIndex(
                name: "IX_investor_applications_PropertyId",
                table: "investor_applications",
                column: "PropertyId");
        }
    }
}
