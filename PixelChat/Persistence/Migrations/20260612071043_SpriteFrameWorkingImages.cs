using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpriteFrameWorkingImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkingContentType",
                table: "SpriteSheetFrameRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "image/png");

            migrationBuilder.AddColumn<byte[]>(
                name: "WorkingData",
                table: "SpriteSheetFrameRecords",
                type: "BLOB",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "WorkingHeight",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkingMargin",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WorkingState",
                table: "SpriteSheetFrameRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<DateTime>(
                name: "WorkingUpdatedAt",
                table: "SpriteSheetFrameRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkingWidth",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkingContentType",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "WorkingData",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "WorkingHeight",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "WorkingMargin",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "WorkingState",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "WorkingUpdatedAt",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "WorkingWidth",
                table: "SpriteSheetFrameRecords");
        }
    }
}
