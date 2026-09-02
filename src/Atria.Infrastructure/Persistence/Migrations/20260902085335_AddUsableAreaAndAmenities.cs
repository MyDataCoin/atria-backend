using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsableAreaAndAmenities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuildingClass",
                table: "properties",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentedUse",
                table: "properties",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Elevator",
                table: "properties",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Heating",
                table: "properties",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Parking",
                table: "properties",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Security",
                table: "properties",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UsableAreaSqM",
                table: "properties",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WallMaterial",
                table: "properties",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuildingClass",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "DocumentedUse",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "Elevator",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "Heating",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "Parking",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "Security",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "UsableAreaSqM",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "WallMaterial",
                table: "properties");
        }
    }
}
