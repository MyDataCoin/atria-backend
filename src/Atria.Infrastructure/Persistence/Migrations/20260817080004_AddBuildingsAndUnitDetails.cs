using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingsAndUnitDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BuildingId",
                table: "properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FloorNumber",
                table: "properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoomCount",
                table: "properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAreaSqM",
                table: "properties",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitNumber",
                table: "properties",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitType",
                table: "properties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "buildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    Address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Developer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    YearBuilt = table.Column<int>(type: "integer", nullable: true),
                    Floors = table.Column<int>(type: "integer", nullable: true),
                    BuildingType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buildings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "property_rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AreaSqM = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_property_rooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_property_rooms_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "building_images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_building_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_building_images_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_properties_BuildingId",
                table: "properties",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_building_images_BuildingId",
                table: "building_images",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_property_rooms_PropertyId",
                table: "property_rooms",
                column: "PropertyId");

            migrationBuilder.AddForeignKey(
                name: "FK_properties_buildings_BuildingId",
                table: "properties",
                column: "BuildingId",
                principalTable: "buildings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_properties_buildings_BuildingId",
                table: "properties");

            migrationBuilder.DropTable(
                name: "building_images");

            migrationBuilder.DropTable(
                name: "property_rooms");

            migrationBuilder.DropTable(
                name: "buildings");

            migrationBuilder.DropIndex(
                name: "IX_properties_BuildingId",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "BuildingId",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "FloorNumber",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "RoomCount",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "TotalAreaSqM",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "UnitNumber",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "UnitType",
                table: "properties");
        }
    }
}
