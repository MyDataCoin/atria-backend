using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLandPlotAndConstructionStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CadastralNumber",
                table: "properties",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConstructionStage",
                table: "properties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EncumbranceCheckedAtUtc",
                table: "properties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFreeOfEncumbrances",
                table: "properties",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LandAreaHectares",
                table: "properties",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LandPlotCode",
                table: "properties",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlannedCompletionDate",
                table: "properties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadinessPercent",
                table: "properties",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CadastralNumber",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "ConstructionStage",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "EncumbranceCheckedAtUtc",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "IsFreeOfEncumbrances",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "LandAreaHectares",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "LandPlotCode",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "PlannedCompletionDate",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "ReadinessPercent",
                table: "properties");
        }
    }
}
