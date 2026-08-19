using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitFeedbackContactIntoEmailAndPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Contact",
                table: "feedback_requests",
                newName: "Email");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "feedback_requests",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Phone",
                table: "feedback_requests");

            migrationBuilder.RenameColumn(
                name: "Email",
                table: "feedback_requests",
                newName: "Contact");
        }
    }
}
