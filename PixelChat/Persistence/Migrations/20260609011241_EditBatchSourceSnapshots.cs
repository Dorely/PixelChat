using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EditBatchSourceSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EditSourceContentType",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "EditSourceData",
                table: "GenerationBatches",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EditSourceHeight",
                table: "GenerationBatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EditSourceWidth",
                table: "GenerationBatches",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EditSourceContentType",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "EditSourceData",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "EditSourceHeight",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "EditSourceWidth",
                table: "GenerationBatches");
        }
    }
}
