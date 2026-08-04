using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOtpRequestSourceIp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestedFromIp",
                table: "otp_codes",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_otp_codes_Phone_CreatedAtUtc",
                table: "otp_codes",
                columns: new[] { "Phone", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_otp_codes_RequestedFromIp_CreatedAtUtc",
                table: "otp_codes",
                columns: new[] { "RequestedFromIp", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_otp_codes_Phone_CreatedAtUtc",
                table: "otp_codes");

            migrationBuilder.DropIndex(
                name: "IX_otp_codes_RequestedFromIp_CreatedAtUtc",
                table: "otp_codes");

            migrationBuilder.DropColumn(
                name: "RequestedFromIp",
                table: "otp_codes");
        }
    }
}
