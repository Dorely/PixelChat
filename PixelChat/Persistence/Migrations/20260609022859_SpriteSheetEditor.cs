using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpriteSheetEditor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveSpriteSheetId",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SpriteSheetDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OutputAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Rows = table.Column<int>(type: "INTEGER", nullable: false),
                    Columns = table.Column<int>(type: "INTEGER", nullable: false),
                    CellWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    CellHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    Padding = table.Column<int>(type: "INTEGER", nullable: false),
                    Gutter = table.Column<int>(type: "INTEGER", nullable: false),
                    Fps = table.Column<int>(type: "INTEGER", nullable: false),
                    Loop = table.Column<bool>(type: "INTEGER", nullable: false),
                    FramesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteSheetDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpriteSheetDefinitions_ArtAssets_OutputAssetId",
                        column: x => x.OutputAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SpriteSheetDefinitions_ArtAssets_SourceAssetId",
                        column: x => x.SourceAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SpriteSheetDefinitions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ActiveSpriteSheetId",
                table: "Projects",
                column: "ActiveSpriteSheetId");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteSheetDefinitions_OutputAssetId",
                table: "SpriteSheetDefinitions",
                column: "OutputAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteSheetDefinitions_ProjectId_UpdatedAt",
                table: "SpriteSheetDefinitions",
                columns: new[] { "ProjectId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SpriteSheetDefinitions_SourceAssetId",
                table: "SpriteSheetDefinitions",
                column: "SourceAssetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpriteSheetDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_Projects_ActiveSpriteSheetId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ActiveSpriteSheetId",
                table: "Projects");
        }
    }
}
