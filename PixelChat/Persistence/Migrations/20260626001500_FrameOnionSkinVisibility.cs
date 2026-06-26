using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260626001500_FrameOnionSkinVisibility")]
    public partial class FrameOnionSkinVisibility : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HideFromOnionSkin",
                table: "Frames",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HideFromOnionSkin",
                table: "Frames");
        }
    }
}
