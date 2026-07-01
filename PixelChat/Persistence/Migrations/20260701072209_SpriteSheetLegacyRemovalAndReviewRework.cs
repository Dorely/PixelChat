using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpriteSheetLegacyRemovalAndReviewRework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename the persisted workspace mode after Compare -> Review.
            migrationBuilder.Sql("UPDATE Projects SET ActiveWorkspaceMode = 'Review' WHERE ActiveWorkspaceMode = 'Compare';");

            // Drop curated review items whose kinds no longer exist (legacy sprite-sheet kinds and whole-batch kind).
            migrationBuilder.Sql("DELETE FROM CompareReviewSetItems WHERE Kind IN ('GenerationBatch', 'SpriteSheet', 'SpriteAnimation', 'SpriteFrame');");

            migrationBuilder.DropTable(
                name: "SpriteSheetFrameRecords");

            migrationBuilder.DropTable(
                name: "SpriteSheetDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_Projects_ActiveSpriteSheetId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ActiveSpriteSheetId",
                table: "Projects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
                    OutputAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BackgroundColor = table.Column<string>(type: "TEXT", nullable: true),
                    BackgroundMode = table.Column<string>(type: "TEXT", nullable: true),
                    CellHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    CellWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    Columns = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Fps = table.Column<int>(type: "INTEGER", nullable: false),
                    FramesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Gutter = table.Column<int>(type: "INTEGER", nullable: false),
                    HorizontalAnchor = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "center"),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Loop = table.Column<bool>(type: "INTEGER", nullable: false),
                    Padding = table.Column<int>(type: "INTEGER", nullable: false),
                    Rows = table.Column<int>(type: "INTEGER", nullable: false),
                    StabilizationJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VerticalAnchor = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "bottom")
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

            migrationBuilder.CreateTable(
                name: "SpriteSheetFrameRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceImageAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SpriteSheetDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppliedScale = table.Column<double>(type: "REAL", nullable: false, defaultValue: 1.0),
                    CellHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    CellWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    CellX = table.Column<int>(type: "INTEGER", nullable: false),
                    CellY = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    FootContactsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    IsKeyframe = table.Column<bool>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Phase = table.Column<double>(type: "REAL", nullable: false),
                    PivotX = table.Column<int>(type: "INTEGER", nullable: false),
                    PivotY = table.Column<int>(type: "INTEGER", nullable: false),
                    PoseName = table.Column<string>(type: "TEXT", nullable: false),
                    PreviewContentType = table.Column<string>(type: "TEXT", nullable: false),
                    PreviewData = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PreviewHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviewWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    QaStatus = table.Column<string>(type: "TEXT", nullable: false),
                    RepairHistoryJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    RootOffsetX = table.Column<int>(type: "INTEGER", nullable: false),
                    RootOffsetY = table.Column<int>(type: "INTEGER", nullable: false),
                    ShapeJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    SourceHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceImageHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceImageWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceImageX = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceImageY = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceX = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceY = table.Column<int>(type: "INTEGER", nullable: false),
                    SpriteHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    SpriteWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    SpriteX = table.Column<int>(type: "INTEGER", nullable: false),
                    SpriteY = table.Column<int>(type: "INTEGER", nullable: false),
                    TranslationX = table.Column<int>(type: "INTEGER", nullable: false),
                    TranslationY = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WorkingContentType = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "image/png"),
                    WorkingData = table.Column<byte[]>(type: "BLOB", nullable: false),
                    WorkingHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkingMargin = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkingState = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "none"),
                    WorkingUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WorkingWidth = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteSheetFrameRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpriteSheetFrameRecords_ArtAssets_SourceImageAssetId",
                        column: x => x.SourceImageAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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

            migrationBuilder.CreateIndex(
                name: "IX_SpriteSheetFrameRecords_ProjectId_SpriteSheetDefinitionId_Index",
                table: "SpriteSheetFrameRecords",
                columns: new[] { "ProjectId", "SpriteSheetDefinitionId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpriteSheetFrameRecords_SourceImageAssetId",
                table: "SpriteSheetFrameRecords",
                column: "SourceImageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteSheetFrameRecords_SpriteSheetDefinitionId",
                table: "SpriteSheetFrameRecords",
                column: "SpriteSheetDefinitionId");
        }
    }
}
