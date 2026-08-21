using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockchainOperationConfirmations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Confirmations",
                table: "blockchain_operations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_blockchain_operations_Status_CreatedAtUtc",
                table: "blockchain_operations",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_blockchain_operations_Status_CreatedAtUtc",
                table: "blockchain_operations");

            migrationBuilder.DropColumn(
                name: "Confirmations",
                table: "blockchain_operations");
        }
    }
}
