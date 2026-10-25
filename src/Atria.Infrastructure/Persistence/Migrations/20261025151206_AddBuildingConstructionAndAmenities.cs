using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingConstructionAndAmenities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConstructionStage",
                table: "buildings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EncumbranceCheckedAtUtc",
                table: "buildings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasElevator",
                table: "buildings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasHeating",
                table: "buildings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasParking",
                table: "buildings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSecurity",
                table: "buildings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFreeOfEncumbrances",
                table: "buildings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlannedCompletionDate",
                table: "buildings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlannedCompletionPrecision",
                table: "buildings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAreaSqM",
                table: "buildings",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConstructionStage",
                table: "buildings");

            migrationBuilder.DropColumn(
                name: "EncumbranceCheckedAtUtc",
                table: "buildings");

            migrationBuilder.DropColumn(
                name: "HasElevator",
                table: "buildings");

            migrationBuilder.DropColumn(
                name: "HasHeating",
                table: "buildings");

            migrationBuilder.DropColumn(
                name: "HasParking",
                table: "buildings");

            migrationBuilder.DropColumn(
                name: "HasSecurity",
                table: "buildings");

            migrationBuilder.DropColumn(
                name: "IsFreeOfEncumbrances",
                table: "buildings");

            migrationBuilder.DropColumn(
                name: "PlannedCompletionDate",
                table: "buildings");

            migrationBuilder.DropColumn(
                name: "PlannedCompletionPrecision",
                table: "buildings");

            migrationBuilder.DropColumn(
                name: "TotalAreaSqM",
                table: "buildings");
        }
    }
}
