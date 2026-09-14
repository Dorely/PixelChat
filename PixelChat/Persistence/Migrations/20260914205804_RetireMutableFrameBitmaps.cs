using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireMutableFrameBitmaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Frames_ArtAssets_BitmapRevisionAssetId",
                table: "Frames");

            migrationBuilder.DropIndex(
                name: "IX_Frames_BitmapRevisionAssetId",
                table: "Frames");

            migrationBuilder.DropIndex(
                name: "IX_Frames_ProjectId_FrameSetId_Index",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "BitmapRevisionAssetId",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "PreviewContentType",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "PreviewData",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "PreviewHeight",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "PreviewWidth",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "WorkingCanvasFinalizationJson",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "WorkingCanvasTransformJson",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "WorkingContentType",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "WorkingData",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "WorkingHeight",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "WorkingMargin",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "WorkingState",
                table: "Frames");

            migrationBuilder.DropColumn(
                name: "WorkingUpdatedAt",
                table: "Frames");

            migrationBuilder.RenameColumn(
                name: "WorkingWidth",
                table: "Frames",
                newName: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Frames_ProjectId_FrameSetId_Index",
                table: "Frames",
                columns: new[] { "ProjectId", "FrameSetId", "Index" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Frames_ProjectId_FrameSetId_Index",
                table: "Frames");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "Frames",
                newName: "WorkingWidth");

            migrationBuilder.AddColumn<Guid>(
                name: "BitmapRevisionAssetId",
                table: "Frames",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewContentType",
                table: "Frames",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "PreviewData",
                table: "Frames",
                type: "BLOB",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "PreviewHeight",
                table: "Frames",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreviewWidth",
                table: "Frames",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WorkingCanvasFinalizationJson",
                table: "Frames",
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
                name: "WorkingContentType",
                table: "Frames",
                type: "TEXT",
                nullable: false,
                defaultValue: "image/png");

            migrationBuilder.AddColumn<byte[]>(
                name: "WorkingData",
                table: "Frames",
                type: "BLOB",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "WorkingHeight",
                table: "Frames",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkingMargin",
                table: "Frames",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WorkingState",
                table: "Frames",
                type: "TEXT",
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<DateTime>(
                name: "WorkingUpdatedAt",
                table: "Frames",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Frames_BitmapRevisionAssetId",
                table: "Frames",
                column: "BitmapRevisionAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Frames_ProjectId_FrameSetId_Index",
                table: "Frames",
                columns: new[] { "ProjectId", "FrameSetId", "Index" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Frames_ArtAssets_BitmapRevisionAssetId",
                table: "Frames",
                column: "BitmapRevisionAssetId",
                principalTable: "ArtAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
