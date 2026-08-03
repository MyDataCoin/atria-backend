using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTravelRuleMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "travel_rule_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    InvestorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenCount = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OriginatorAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BeneficiaryAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CounterpartyVasp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OriginatorName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    OriginatorDocumentNumber = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    OriginatorNationality = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BeneficiaryName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    TransactionHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Payload = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CounterpartyReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_travel_rule_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_travel_rule_messages_PropertyId",
                table: "travel_rule_messages",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_travel_rule_messages_Status",
                table: "travel_rule_messages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_travel_rule_messages_TransactionHash_OriginatorAddress_Bene~",
                table: "travel_rule_messages",
                columns: new[] { "TransactionHash", "OriginatorAddress", "BeneficiaryAddress" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "travel_rule_messages");
        }
    }
}
