using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReliableOutpaintFinalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CanvasPreparationExpiresAt",
                table: "SpriteEditSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CanvasPreparationId",
                table: "SpriteEditSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanvasPreparationTransformJson",
                table: "SpriteEditSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "EditLogicalMaskData",
                table: "GenerationBatches",
                type: "BLOB",
                nullable: true);

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

            migrationBuilder.AddColumn<string>(
                name: "WorkingCanvasFinalizationJson",
                table: "Frames",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanvasPreparationExpiresAt",
                table: "SpriteEditSessions");

            migrationBuilder.DropColumn(
                name: "CanvasPreparationId",
                table: "SpriteEditSessions");

            migrationBuilder.DropColumn(
                name: "CanvasPreparationTransformJson",
                table: "SpriteEditSessions");

            migrationBuilder.DropColumn(
                name: "EditLogicalMaskData",
                table: "GenerationBatches");

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

            migrationBuilder.DropColumn(
                name: "WorkingCanvasFinalizationJson",
                table: "Frames");
        }
    }
}
