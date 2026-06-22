using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpriteFrameSourceImageProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceImageAssetId",
                table: "SpriteSheetFrameRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceImageHeight",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceImageWidth",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceImageX",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceImageY",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_SpriteSheetFrameRecords_SourceImageAssetId",
                table: "SpriteSheetFrameRecords",
                column: "SourceImageAssetId");

            migrationBuilder.AddForeignKey(
                name: "FK_SpriteSheetFrameRecords_ArtAssets_SourceImageAssetId",
                table: "SpriteSheetFrameRecords",
                column: "SourceImageAssetId",
                principalTable: "ArtAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SpriteSheetFrameRecords_ArtAssets_SourceImageAssetId",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropIndex(
                name: "IX_SpriteSheetFrameRecords_SourceImageAssetId",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "SourceImageAssetId",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "SourceImageHeight",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "SourceImageWidth",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "SourceImageX",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "SourceImageY",
                table: "SpriteSheetFrameRecords");
        }
    }
}
