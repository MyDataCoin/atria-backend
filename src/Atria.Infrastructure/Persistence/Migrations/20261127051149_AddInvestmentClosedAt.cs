using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentClosedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAtUtc",
                table: "investments",
                type: "timestamp with time zone",
                nullable: true);

            // Holdings already ended before the column existed: the last write to the row is the
            // closest record of when (status 5 = Withdrawn, 6 = Annulled).
            migrationBuilder.Sql(
                "UPDATE investments SET \"ClosedAtUtc\" = COALESCE(\"UpdatedAtUtc\", \"CreatedAtUtc\") "
                + "WHERE \"Status\" IN (5, 6) AND \"ClosedAtUtc\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "investments");
        }
    }
}
