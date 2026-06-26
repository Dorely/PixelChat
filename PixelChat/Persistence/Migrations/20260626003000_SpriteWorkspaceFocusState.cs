using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260626003000_SpriteWorkspaceFocusState")]
    public partial class SpriteWorkspaceFocusState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveSpriteMode",
                table: "Projects",
                type: "TEXT",
                nullable: false,
                defaultValue: "source");

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveSpriteSourceAssetId",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveSpriteFrameId",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveSpriteRegionIdsJson",
                table: "Projects",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveSpriteMode",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ActiveSpriteSourceAssetId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ActiveSpriteFrameId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ActiveSpriteRegionIdsJson",
                table: "Projects");
        }
    }
}
