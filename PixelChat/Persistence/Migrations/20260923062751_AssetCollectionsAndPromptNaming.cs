using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetCollectionsAndPromptNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssetNamingMode",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: false,
                defaultValue: "Existing");

            migrationBuilder.AddColumn<Guid>(
                name: "CollectionId",
                table: "ArtAssets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCollections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetCollections_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtAssets_CollectionId",
                table: "ArtAssets",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtAssets_ProjectId_CollectionId",
                table: "ArtAssets",
                columns: new[] { "ProjectId", "CollectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetCollections_ProjectId_Name",
                table: "AssetCollections",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ArtAssets_AssetCollections_CollectionId",
                table: "ArtAssets",
                column: "CollectionId",
                principalTable: "AssetCollections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ArtAssets_AssetCollections_CollectionId",
                table: "ArtAssets");

            migrationBuilder.DropTable(
                name: "AssetCollections");

            migrationBuilder.DropIndex(
                name: "IX_ArtAssets_CollectionId",
                table: "ArtAssets");

            migrationBuilder.DropIndex(
                name: "IX_ArtAssets_ProjectId_CollectionId",
                table: "ArtAssets");

            migrationBuilder.DropColumn(
                name: "AssetNamingMode",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "CollectionId",
                table: "ArtAssets");
        }
    }
}
