using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCryptoPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "crypto_deposits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    TransferKey = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    TransactionHash = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    FromAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crypto_deposits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "crypto_invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    PayToAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayAmount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    RateKgs = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AmountKgs = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TransactionHash = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    FromAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crypto_invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "crypto_watch_cursors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Position = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crypto_watch_cursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payment_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsdtRateKgs = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Bep20Address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Trc20Address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    KgstContractAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_crypto_deposits_InvoiceId",
                table: "crypto_deposits",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_crypto_deposits_Method_TransferKey",
                table: "crypto_deposits",
                columns: new[] { "Method", "TransferKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crypto_deposits_OccurredAtUtc",
                table: "crypto_deposits",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_crypto_invoices_InvestmentId",
                table: "crypto_invoices",
                column: "InvestmentId");

            migrationBuilder.CreateIndex(
                name: "IX_crypto_invoices_Method_Status_PayAmount",
                table: "crypto_invoices",
                columns: new[] { "Method", "Status", "PayAmount" });

            migrationBuilder.CreateIndex(
                name: "ux_crypto_invoices_pending_amount",
                table: "crypto_invoices",
                columns: new[] { "Method", "PayAmount" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_crypto_watch_cursors_Key",
                table: "crypto_watch_cursors",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crypto_deposits");

            migrationBuilder.DropTable(
                name: "crypto_invoices");

            migrationBuilder.DropTable(
                name: "crypto_watch_cursors");

            migrationBuilder.DropTable(
                name: "payment_settings");
        }
    }
}
