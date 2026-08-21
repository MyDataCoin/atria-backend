using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Каждой существующей строке — СВОЯ семья. Общий дефолт (нулевой Guid) склеил бы все
            // токены всех пользователей в одну цепочку, и первое же срабатывание защиты от повторного
            // использования разлогинило бы разом вообще всех. Отдельная семья на строку сохраняет
            // текущие сессии и делает поведение таким же, каким оно было до появления колонки.
            migrationBuilder.Sql(
                """UPDATE refresh_tokens SET "FamilyId" = gen_random_uuid();""");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_FamilyId",
                table: "refresh_tokens",
                column: "FamilyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_FamilyId",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "refresh_tokens");
        }
    }
}
