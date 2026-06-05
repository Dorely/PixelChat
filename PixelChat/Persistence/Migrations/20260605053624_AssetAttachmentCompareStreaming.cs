using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetAttachmentCompareStreaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveAssetId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "IsReference",
                table: "ArtAssets");

            migrationBuilder.DropColumn(
                name: "IsRejected",
                table: "ArtAssets");

            migrationBuilder.AddColumn<string>(
                name: "OutputErrorsJson",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutputErrorsJson",
                table: "GenerationBatches");

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveAssetId",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReference",
                table: "ArtAssets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRejected",
                table: "ArtAssets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
