using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeTrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exchange_trades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    SequenceNo = table.Column<long>(type: "bigint", nullable: false),
                    ExchangeTradeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Chain = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenContract = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TransactionHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    BlockNumber = table.Column<long>(type: "bigint", nullable: true),
                    SellerAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BuyerAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenCount = table.Column<long>(type: "bigint", nullable: false),
                    PricePerToken = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    TradedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CounterpartyVasp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BeneficiaryName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CustodyAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MatchState = table.Column<int>(type: "integer", nullable: false),
                    MatchDetail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    MatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversesEventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsReversed = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_trades", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_trades_EventId",
                table: "exchange_trades",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exchange_trades_PropertyId_MatchState",
                table: "exchange_trades",
                columns: new[] { "PropertyId", "MatchState" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_trades_PropertyId_SequenceNo",
                table: "exchange_trades",
                columns: new[] { "PropertyId", "SequenceNo" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_trades_TransactionHash",
                table: "exchange_trades",
                column: "TransactionHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exchange_trades");
        }
    }
}
