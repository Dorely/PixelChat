using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpriteSheetSecondPass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpriteSheetFrameRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SpriteSheetDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    SourceX = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceY = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    CellX = table.Column<int>(type: "INTEGER", nullable: false),
                    CellY = table.Column<int>(type: "INTEGER", nullable: false),
                    CellWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    CellHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    SpriteX = table.Column<int>(type: "INTEGER", nullable: false),
                    SpriteY = table.Column<int>(type: "INTEGER", nullable: false),
                    SpriteWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    SpriteHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviewContentType = table.Column<string>(type: "TEXT", nullable: false),
                    PreviewData = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PreviewWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviewHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteSheetFrameRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpriteSheetFrameRecords_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SpriteSheetFrameRecords_SpriteSheetDefinitions_SpriteSheetDefinitionId",
                        column: x => x.SpriteSheetDefinitionId,
                        principalTable: "SpriteSheetDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpriteSheetFrameRecords_ProjectId_SpriteSheetDefinitionId_Index",
                table: "SpriteSheetFrameRecords",
                columns: new[] { "ProjectId", "SpriteSheetDefinitionId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpriteSheetFrameRecords_SpriteSheetDefinitionId",
                table: "SpriteSheetFrameRecords",
                column: "SpriteSheetDefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpriteSheetFrameRecords");
        }
    }
}
