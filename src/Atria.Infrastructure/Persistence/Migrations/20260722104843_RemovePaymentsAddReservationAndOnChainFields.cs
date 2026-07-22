using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemovePaymentsAddReservationAndOnChainFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IssuerWalletAddress",
                table: "properties",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenChain",
                table: "properties",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenContractAddress",
                table: "properties",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "properties",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<int>(
                name: "OnChainStatus",
                table: "investments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PricePerToken",
                table: "investments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReservedUntilUtc",
                table: "investments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "TokenContractAddress",
                table: "investments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransactionHash",
                table: "investments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WalletAddress",
                table: "investments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IssuerWalletAddress",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "TokenChain",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "TokenContractAddress",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "OnChainStatus",
                table: "investments");

            migrationBuilder.DropColumn(
                name: "PricePerToken",
                table: "investments");

            migrationBuilder.DropColumn(
                name: "ReservedUntilUtc",
                table: "investments");

            migrationBuilder.DropColumn(
                name: "TokenContractAddress",
                table: "investments");

            migrationBuilder.DropColumn(
                name: "TransactionHash",
                table: "investments");

            migrationBuilder.DropColumn(
                name: "WalletAddress",
                table: "investments");
        }
    }
}
