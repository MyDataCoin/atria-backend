using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeTokenAmountsDivisible : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "TokenCount",
                table: "travel_rule_messages",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "TokenCount",
                table: "refund_obligations",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalTokens",
                table: "properties",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "AvailableTokens",
                table: "properties",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalTokens",
                table: "payout_runs",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "TokenCount",
                table: "payout_items",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "TokenCount",
                table: "investments",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalTokens",
                table: "holder_snapshots",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "TokenCount",
                table: "holder_snapshot_rows",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "TokenCount",
                table: "holder_positions",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "TokenCount",
                table: "travel_rule_messages",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,2)",
                oldPrecision: 28,
                oldScale: 2);

            migrationBuilder.AlterColumn<long>(
                name: "TokenCount",
                table: "refund_obligations",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,2)",
                oldPrecision: 28,
                oldScale: 2);

            migrationBuilder.AlterColumn<long>(
                name: "TotalTokens",
                table: "properties",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,2)",
                oldPrecision: 28,
                oldScale: 2);

            migrationBuilder.AlterColumn<long>(
                name: "AvailableTokens",
                table: "properties",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,2)",
                oldPrecision: 28,
                oldScale: 2);

            migrationBuilder.AlterColumn<long>(
                name: "TotalTokens",
                table: "payout_runs",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,2)",
                oldPrecision: 28,
                oldScale: 2);

            migrationBuilder.AlterColumn<long>(
                name: "TokenCount",
                table: "payout_items",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,2)",
                oldPrecision: 28,
                oldScale: 2);

            migrationBuilder.AlterColumn<long>(
                name: "TokenCount",
                table: "investments",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,2)",
                oldPrecision: 28,
                oldScale: 2);

            migrationBuilder.AlterColumn<long>(
                name: "TotalTokens",
                table: "holder_snapshots",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,2)",
                oldPrecision: 28,
                oldScale: 2);

            migrationBuilder.AlterColumn<long>(
                name: "TokenCount",
                table: "holder_snapshot_rows",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,2)",
                oldPrecision: 28,
                oldScale: 2);

            migrationBuilder.AlterColumn<long>(
                name: "TokenCount",
                table: "holder_positions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,2)",
                oldPrecision: 28,
                oldScale: 2);
        }
    }
}
