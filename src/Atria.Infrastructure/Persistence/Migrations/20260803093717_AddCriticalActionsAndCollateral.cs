using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCriticalActionsAndCollateral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CollateralAppraiser",
                table: "properties",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollateralManagerUserId",
                table: "properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CollateralValue",
                table: "properties",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CollateralValuedAtUtc",
                table: "properties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EncumbranceRegisteredAtUtc",
                table: "properties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncumbranceRegistrationNumber",
                table: "properties",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssueRegistrationNumber",
                table: "properties",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "critical_actions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_critical_actions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_critical_actions_Status_Kind_TargetId",
                table: "critical_actions",
                columns: new[] { "Status", "Kind", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "critical_actions");

            migrationBuilder.DropColumn(
                name: "CollateralAppraiser",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "CollateralManagerUserId",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "CollateralValue",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "CollateralValuedAtUtc",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "EncumbranceRegisteredAtUtc",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "EncumbranceRegistrationNumber",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "IssueRegistrationNumber",
                table: "properties");
        }
    }
}
