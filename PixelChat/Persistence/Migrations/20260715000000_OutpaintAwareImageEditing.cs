using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260715000000_OutpaintAwareImageEditing")]
public partial class OutpaintAwareImageEditing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "EditCanvasTransformJson",
            table: "GenerationBatches",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "WorkingCanvasTransformJson",
            table: "Frames",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "CanvasOptionsJson",
            table: "SpriteEditSessions",
            type: "TEXT",
            nullable: false,
            defaultValue: "{}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "EditCanvasTransformJson", table: "GenerationBatches");
        migrationBuilder.DropColumn(name: "WorkingCanvasTransformJson", table: "Frames");
        migrationBuilder.DropColumn(name: "CanvasOptionsJson", table: "SpriteEditSessions");
    }
}
