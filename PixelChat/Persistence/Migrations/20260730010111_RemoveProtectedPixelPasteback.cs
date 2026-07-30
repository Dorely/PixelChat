using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProtectedPixelPasteback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EditLogicalSourceContentType",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "EditLogicalSourceData",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "EditLogicalSourceHeight",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "EditLogicalSourceWidth",
                table: "GenerationBatches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EditLogicalSourceContentType",
                table: "GenerationBatches",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "EditLogicalSourceData",
                table: "GenerationBatches",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EditLogicalSourceHeight",
                table: "GenerationBatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EditLogicalSourceWidth",
                table: "GenerationBatches",
                type: "INTEGER",
                nullable: true);
        }
    }
}
