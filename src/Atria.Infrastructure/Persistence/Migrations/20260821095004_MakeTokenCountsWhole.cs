using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeTokenCountsWhole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every share count becomes a whole number, because the token contract's decimals() is
            // zero and a fractional holding could never be minted against it.
            //
            // The conversion refuses to run on data that still holds fractions rather than rounding
            // them away. Rounding here looks harmless and is not: floor every row and the pool stops
            // adding up — an issue of 57 with 56.47 unplaced and a 0.53 application becomes 57 with
            // 56 unplaced and nothing placed, and the missing share is gone with no record that it
            // ever existed. Fractional rows are settled deliberately first (scripts/retire-fractional-
            // token-data.sql: reserved applications cancelled, pools re-cut to the new issue size),
            // and only then is the type changed.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    entry   record;
    fractional bigint;
    offending  text[] := '{}';
BEGIN
    FOR entry IN
        SELECT * FROM (VALUES
            ('properties',           'TotalTokens'),
            ('properties',           'AvailableTokens'),
            ('investments',          'TokenCount'),
            ('whitelist_entries',    'TokenCount'),
            ('mint_lists',           'TotalTokens'),
            ('mint_list_items',      'TokenCount'),
            ('holder_positions',     'TokenCount'),
            ('holder_snapshots',     'TotalTokens'),
            ('holder_snapshot_rows', 'TokenCount'),
            ('payout_runs',          'TotalTokens'),
            ('payout_items',         'TokenCount'),
            ('refund_obligations',   'TokenCount'),
            ('travel_rule_messages', 'TokenCount')
        ) AS cols(tbl, col)
    LOOP
        -- to_regclass резолвит имя по search_path, поэтому таблица, которой ещё нет в этой
        -- схеме, просто пропускается: миграция должна проходить и на базе, накатанной не с нуля.
        CONTINUE WHEN to_regclass(format('%I', entry.tbl)) IS NULL;

        EXECUTE format('SELECT count(*) FROM %I WHERE %I <> trunc(%I)', entry.tbl, entry.col, entry.col)
            INTO fractional;

        IF fractional > 0 THEN
            offending := offending || format('%s.%s (%s rows)', entry.tbl, entry.col, fractional);
        END IF;
    END LOOP;

    IF array_length(offending, 1) > 0 THEN
        RAISE EXCEPTION
            'Fractional share counts remain in: %. Settle them first (scripts/retire-fractional-token-data.sql), then re-run this migration.',
            array_to_string(offending, ', ');
    END IF;
END $$;");

            // Exact casts: the check above has already established there is nothing to round.
            AlterToWholeShares(migrationBuilder, "properties", "TotalTokens");
            AlterToWholeShares(migrationBuilder, "properties", "AvailableTokens");
            AlterToWholeShares(migrationBuilder, "investments", "TokenCount");
            AlterToWholeShares(migrationBuilder, "whitelist_entries", "TokenCount");
            AlterToWholeShares(migrationBuilder, "mint_lists", "TotalTokens");
            AlterToWholeShares(migrationBuilder, "mint_list_items", "TokenCount");
            AlterToWholeShares(migrationBuilder, "holder_positions", "TokenCount");
            AlterToWholeShares(migrationBuilder, "holder_snapshots", "TotalTokens");
            AlterToWholeShares(migrationBuilder, "holder_snapshot_rows", "TokenCount");
            AlterToWholeShares(migrationBuilder, "payout_runs", "TotalTokens");
            AlterToWholeShares(migrationBuilder, "payout_items", "TokenCount");
            AlterToWholeShares(migrationBuilder, "refund_obligations", "TokenCount");
            AlterToWholeShares(migrationBuilder, "travel_rule_messages", "TokenCount");

            // Defaults to a single share: the smallest minimum there is, so no existing offering
            // silently acquires an entry barrier it was never given. Admins raise it per offering.
            migrationBuilder.AddColumn<long>(
                name: "MinPurchaseTokens",
                table: "properties",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);
        }

        // AlterColumn alone emits ALTER COLUMN ... TYPE bigint, which PostgreSQL refuses on a numeric
        // column without being told how to cast. The USING clause is that instruction.
        private static void AlterToWholeShares(MigrationBuilder migrationBuilder, string table, string column)
            => migrationBuilder.Sql(
                $@"ALTER TABLE ""{table}"" ALTER COLUMN ""{column}"" TYPE bigint USING ""{column}""::bigint;");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinPurchaseTokens",
                table: "properties");

            migrationBuilder.AlterColumn<decimal>(
                name: "TokenCount",
                table: "whitelist_entries",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

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
                name: "TotalTokens",
                table: "mint_lists",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "TokenCount",
                table: "mint_list_items",
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
    }
}
