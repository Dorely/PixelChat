using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreserveRawProviderImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RawProviderContentType",
                table: "ArtAssets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RawProviderData",
                table: "ArtAssets",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RawProviderContentType",
                table: "ArtAssets");

            migrationBuilder.DropColumn(
                name: "RawProviderData",
                table: "ArtAssets");
        }
    }
}
