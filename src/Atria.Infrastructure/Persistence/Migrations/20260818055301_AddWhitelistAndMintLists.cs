using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhitelistAndMintLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mint_lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenContractAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TokenChain = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: false),
                    ItemCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExecutedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mint_lists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "whitelist_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenCount = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: false),
                    WalletAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MintListId = table.Column<Guid>(type: "uuid", nullable: true),
                    MintedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExclusionReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whitelist_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mint_list_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MintListId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhitelistEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestorId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenCount = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mint_list_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mint_list_items_mint_lists_MintListId",
                        column: x => x.MintListId,
                        principalTable: "mint_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mint_list_items_MintListId",
                table: "mint_list_items",
                column: "MintListId");

            migrationBuilder.CreateIndex(
                name: "IX_mint_list_items_MintListId_WhitelistEntryId",
                table: "mint_list_items",
                columns: new[] { "MintListId", "WhitelistEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mint_lists_Number",
                table: "mint_lists",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mint_lists_PropertyId",
                table: "mint_lists",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_whitelist_entries_InvestmentId",
                table: "whitelist_entries",
                column: "InvestmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_whitelist_entries_MintListId",
                table: "whitelist_entries",
                column: "MintListId");

            migrationBuilder.CreateIndex(
                name: "IX_whitelist_entries_PropertyId_Status",
                table: "whitelist_entries",
                columns: new[] { "PropertyId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mint_list_items");

            migrationBuilder.DropTable(
                name: "whitelist_entries");

            migrationBuilder.DropTable(
                name: "mint_lists");
        }
    }
}
