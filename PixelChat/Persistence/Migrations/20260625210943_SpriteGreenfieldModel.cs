using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpriteGreenfieldModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoordinateSpace",
                table: "ImageMasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "source");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "ImageMasks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "OwnerKind",
                table: "ImageMasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "asset");

            migrationBuilder.CreateTable(
                name: "FrameSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    OrderedFrameIdsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    DefaultCellWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultCellHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    PlaybackSettingsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    AlignmentSettingsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrameSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FrameSets_ArtAssets_SourceAssetId",
                        column: x => x.SourceAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FrameSets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HistoryTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "user"),
                    CheckpointId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OperationsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "running")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoryTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoryTasks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpriteRegions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    X = table.Column<int>(type: "INTEGER", nullable: false),
                    Y = table.Column<int>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    ShapeJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    RegionType = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "frame"),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteRegions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpriteRegions_ArtAssets_SourceAssetId",
                        column: x => x.SourceAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SpriteRegions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SheetLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FrameSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Rows = table.Column<int>(type: "INTEGER", nullable: false),
                    Columns = table.Column<int>(type: "INTEGER", nullable: false),
                    CellWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    CellHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    Padding = table.Column<int>(type: "INTEGER", nullable: false),
                    Gutter = table.Column<int>(type: "INTEGER", nullable: false),
                    OuterMargin = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordering = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "rowMajor"),
                    Fps = table.Column<int>(type: "INTEGER", nullable: false),
                    Loop = table.Column<bool>(type: "INTEGER", nullable: false),
                    HorizontalAnchor = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "center"),
                    VerticalAnchor = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "bottom"),
                    BackgroundMode = table.Column<string>(type: "TEXT", nullable: true),
                    BackgroundColor = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SheetLayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SheetLayouts_FrameSets_FrameSetId",
                        column: x => x.FrameSetId,
                        principalTable: "FrameSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SheetLayouts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Frames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FrameSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceRegionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceX = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceY = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    LogicalWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    LogicalHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentOffsetX = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentOffsetY = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    ShapeJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    WorkingState = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "none"),
                    WorkingContentType = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "image/png"),
                    WorkingData = table.Column<byte[]>(type: "BLOB", nullable: false),
                    WorkingWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkingHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkingMargin = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkingUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PreviewContentType = table.Column<string>(type: "TEXT", nullable: false),
                    PreviewData = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PreviewWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviewHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    BitmapRevisionAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Frames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Frames_ArtAssets_BitmapRevisionAssetId",
                        column: x => x.BitmapRevisionAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Frames_FrameSets_FrameSetId",
                        column: x => x.FrameSetId,
                        principalTable: "FrameSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Frames_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Frames_SpriteRegions_SourceRegionId",
                        column: x => x.SourceRegionId,
                        principalTable: "SpriteRegions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StandaloneAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceRegionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OutputAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LogicalWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    LogicalHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentOffsetX = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentOffsetY = table.Column<int>(type: "INTEGER", nullable: false),
                    LinkedToSource = table.Column<bool>(type: "INTEGER", nullable: false),
                    BitmapRevisionAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandaloneAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandaloneAssets_ArtAssets_BitmapRevisionAssetId",
                        column: x => x.BitmapRevisionAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StandaloneAssets_ArtAssets_OutputAssetId",
                        column: x => x.OutputAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StandaloneAssets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StandaloneAssets_SpriteRegions_SourceRegionId",
                        column: x => x.SourceRegionId,
                        principalTable: "SpriteRegions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BuiltSheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SheetLayoutId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OutputAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    LinkedFrameIdsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuiltSheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuiltSheets_ArtAssets_OutputAssetId",
                        column: x => x.OutputAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BuiltSheets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuiltSheets_SheetLayouts_SheetLayoutId",
                        column: x => x.SheetLayoutId,
                        principalTable: "SheetLayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Anchors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FrameId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    X = table.Column<int>(type: "INTEGER", nullable: false),
                    Y = table.Column<int>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "manual"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anchors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Anchors_Frames_FrameId",
                        column: x => x.FrameId,
                        principalTable: "Frames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Anchors_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImageMasks_OwnerKind_OwnerId",
                table: "ImageMasks",
                columns: new[] { "OwnerKind", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Anchors_FrameId_Name",
                table: "Anchors",
                columns: new[] { "FrameId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Anchors_ProjectId",
                table: "Anchors",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_BuiltSheets_OutputAssetId",
                table: "BuiltSheets",
                column: "OutputAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_BuiltSheets_ProjectId_UpdatedAt",
                table: "BuiltSheets",
                columns: new[] { "ProjectId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BuiltSheets_SheetLayoutId",
                table: "BuiltSheets",
                column: "SheetLayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_Frames_BitmapRevisionAssetId",
                table: "Frames",
                column: "BitmapRevisionAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Frames_FrameSetId",
                table: "Frames",
                column: "FrameSetId");

            migrationBuilder.CreateIndex(
                name: "IX_Frames_ProjectId_FrameSetId_Index",
                table: "Frames",
                columns: new[] { "ProjectId", "FrameSetId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Frames_SourceRegionId",
                table: "Frames",
                column: "SourceRegionId");

            migrationBuilder.CreateIndex(
                name: "IX_FrameSets_ProjectId_UpdatedAt",
                table: "FrameSets",
                columns: new[] { "ProjectId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FrameSets_SourceAssetId",
                table: "FrameSets",
                column: "SourceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoryTasks_ProjectId_StartedAt",
                table: "HistoryTasks",
                columns: new[] { "ProjectId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SheetLayouts_FrameSetId",
                table: "SheetLayouts",
                column: "FrameSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SheetLayouts_ProjectId_FrameSetId",
                table: "SheetLayouts",
                columns: new[] { "ProjectId", "FrameSetId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpriteRegions_ProjectId_SourceAssetId_Order",
                table: "SpriteRegions",
                columns: new[] { "ProjectId", "SourceAssetId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_SpriteRegions_SourceAssetId",
                table: "SpriteRegions",
                column: "SourceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_StandaloneAssets_BitmapRevisionAssetId",
                table: "StandaloneAssets",
                column: "BitmapRevisionAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_StandaloneAssets_OutputAssetId",
                table: "StandaloneAssets",
                column: "OutputAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_StandaloneAssets_ProjectId_CreatedAt",
                table: "StandaloneAssets",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StandaloneAssets_SourceRegionId",
                table: "StandaloneAssets",
                column: "SourceRegionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Anchors");

            migrationBuilder.DropTable(
                name: "BuiltSheets");

            migrationBuilder.DropTable(
                name: "HistoryTasks");

            migrationBuilder.DropTable(
                name: "StandaloneAssets");

            migrationBuilder.DropTable(
                name: "Frames");

            migrationBuilder.DropTable(
                name: "SheetLayouts");

            migrationBuilder.DropTable(
                name: "SpriteRegions");

            migrationBuilder.DropTable(
                name: "FrameSets");

            migrationBuilder.DropIndex(
                name: "IX_ImageMasks_OwnerKind_OwnerId",
                table: "ImageMasks");

            migrationBuilder.DropColumn(
                name: "CoordinateSpace",
                table: "ImageMasks");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "ImageMasks");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "ImageMasks");
        }
    }
}
