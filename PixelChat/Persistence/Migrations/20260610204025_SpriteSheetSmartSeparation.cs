using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpriteSheetSmartSeparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShapeJson",
                table: "SpriteSheetFrameRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "HorizontalAnchor",
                table: "SpriteSheetDefinitions",
                type: "TEXT",
                nullable: false,
                defaultValue: "center");

            migrationBuilder.AddColumn<string>(
                name: "VerticalAnchor",
                table: "SpriteSheetDefinitions",
                type: "TEXT",
                nullable: false,
                defaultValue: "bottom");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShapeJson",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "HorizontalAnchor",
                table: "SpriteSheetDefinitions");

            migrationBuilder.DropColumn(
                name: "VerticalAnchor",
                table: "SpriteSheetDefinitions");
        }
    }
}
