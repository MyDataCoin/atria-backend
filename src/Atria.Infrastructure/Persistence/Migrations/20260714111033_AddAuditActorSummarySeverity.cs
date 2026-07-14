using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditActorSummarySeverity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorName",
                table: "audit_log",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Severity",
                table: "audit_log",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "audit_log",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_EventType",
                table: "audit_log",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_OccurredOnUtc",
                table: "audit_log",
                column: "OccurredOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_Severity",
                table: "audit_log",
                column: "Severity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_log_EventType",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "IX_audit_log_OccurredOnUtc",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "IX_audit_log_Severity",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "ActorName",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "Severity",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "audit_log");
        }
    }
}
