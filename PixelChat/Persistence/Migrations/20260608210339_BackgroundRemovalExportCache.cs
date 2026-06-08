using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackgroundRemovalExportCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackgroundRemovalExportCaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceImageSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    RemovalMethod = table.Column<string>(type: "TEXT", nullable: false),
                    ModelName = table.Column<string>(type: "TEXT", nullable: false),
                    RembgPackageVersion = table.Column<string>(type: "TEXT", nullable: false),
                    AlphaMatting = table.Column<bool>(type: "INTEGER", nullable: false),
                    OptionsHash = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    TransparentPixels = table.Column<int>(type: "INTEGER", nullable: false),
                    SemiTransparentPixels = table.Column<int>(type: "INTEGER", nullable: false),
                    OpaquePixels = table.Column<int>(type: "INTEGER", nullable: false),
                    ActualBackend = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundRemovalExportCaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackgroundRemovalExportCaches_ArtAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundRemovalExportCaches_AssetId_SourceImageSha256_RemovalMethod_ModelName_RembgPackageVersion_AlphaMatting_OptionsHash",
                table: "BackgroundRemovalExportCaches",
                columns: new[] { "AssetId", "SourceImageSha256", "RemovalMethod", "ModelName", "RembgPackageVersion", "AlphaMatting", "OptionsHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundRemovalExportCaches_ProjectId_AssetId",
                table: "BackgroundRemovalExportCaches",
                columns: new[] { "ProjectId", "AssetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackgroundRemovalExportCaches");
        }
    }
}
