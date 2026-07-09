using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplacePropertyIsActiveWithStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "properties");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "properties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing properties were live before the status column existed: mark them Open (1)
            // rather than the Draft (0) default, so they stay visible/investable after the migration.
            migrationBuilder.Sql("UPDATE properties SET \"Status\" = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "properties");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
