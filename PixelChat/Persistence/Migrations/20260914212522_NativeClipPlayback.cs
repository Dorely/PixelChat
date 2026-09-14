using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeClipPlayback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlignmentSettingsJson",
                table: "FrameSets");

            migrationBuilder.DropColumn(
                name: "PlaybackSettingsJson",
                table: "FrameSets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlignmentSettingsJson",
                table: "FrameSets",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "PlaybackSettingsJson",
                table: "FrameSets",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");
        }
    }
}
