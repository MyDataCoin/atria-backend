using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceReceivedAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReceivedAmount",
                table: "crypto_invoices",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            // Paid invoices: what actually arrived is the sum of the deposits matched to them; one
            // with no matched deposit was paid exactly as asked.
            migrationBuilder.Sql("""
                UPDATE crypto_invoices i
                SET "ReceivedAmount" = COALESCE(
                    (SELECT SUM(d."Amount") FROM crypto_deposits d WHERE d."InvoiceId" = i."Id"),
                    i."PayAmount")
                WHERE i."Status" = 1 AND i."ReceivedAmount" IS NULL;
                """);

            // Placements the platform minted before the allocation recorded where the shares went:
            // the wallet comes from the allocation that issued them, the contract from the issue.
            migrationBuilder.Sql("""
                UPDATE investments inv
                SET "WalletAddress" = o."Payload"::jsonb ->> 'wallet',
                    "TokenContractAddress" = p."TokenContractAddress"
                FROM blockchain_operations o, properties p
                WHERE o."Type" = 2
                  AND o."TransactionRef" = inv."TransactionHash"
                  AND (o."Payload"::jsonb ->> 'investmentId') = inv."Id"::text
                  AND p."Id" = inv."PropertyId"
                  AND p."TokenContractAddress" IS NOT NULL
                  AND inv."WalletAddress" IS NULL
                  AND inv."TransactionHash" IS NOT NULL
                  AND (o."Payload"::jsonb ->> 'wallet') IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceivedAmount",
                table: "crypto_invoices");
        }
    }
}
