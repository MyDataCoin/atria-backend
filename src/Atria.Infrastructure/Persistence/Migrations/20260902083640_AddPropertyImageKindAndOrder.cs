using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Existing images all land on SortOrder 0. Their relative order is then whatever the database
    /// returns, which is no worse than before — there was no order at all — but it does mean an
    /// admin has to set the cover explicitly on any object that already had photos.
    /// Kind defaults to Photo, which is what every existing image is: those objects are built.
    /// </remarks>
    public partial class AddPropertyImageKindAndOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Caption",
                table: "property_images",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "property_images",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "property_images",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Caption",
                table: "property_images");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "property_images");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "property_images");
        }
    }
}
