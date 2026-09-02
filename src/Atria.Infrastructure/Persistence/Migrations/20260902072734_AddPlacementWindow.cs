using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlacementWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PlacementClosesAtUtc",
                table: "properties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlacementExtensionCount",
                table: "properties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlacementOpensAtUtc",
                table: "properties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetAmount",
                table: "properties",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_properties_Status_PlacementClosesAtUtc",
                table: "properties",
                columns: new[] { "Status", "PlacementClosesAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_properties_Status_PlacementOpensAtUtc",
                table: "properties",
                columns: new[] { "Status", "PlacementOpensAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_properties_Status_PlacementClosesAtUtc",
                table: "properties");

            migrationBuilder.DropIndex(
                name: "IX_properties_Status_PlacementOpensAtUtc",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "PlacementClosesAtUtc",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "PlacementExtensionCount",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "PlacementOpensAtUtc",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "TargetAmount",
                table: "properties");
        }
    }
}
