using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetAnimationPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AppliedScale",
                table: "SpriteSheetFrameRecords",
                type: "REAL",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FootContactsJson",
                table: "SpriteSheetFrameRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "IsKeyframe",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "Phase",
                table: "SpriteSheetFrameRecords",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "PivotX",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PivotY",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PoseName",
                table: "SpriteSheetFrameRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QaStatus",
                table: "SpriteSheetFrameRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RepairHistoryJson",
                table: "SpriteSheetFrameRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "RootOffsetX",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RootOffsetY",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceAnimationCandidateId",
                table: "SpriteSheetFrameRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceAnimationJobId",
                table: "SpriteSheetFrameRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TranslationX",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TranslationY",
                table: "SpriteSheetFrameRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AssetProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CanonicalAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StyleAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    AssetType = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "unit"),
                    StructureType = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "biped"),
                    ChromaColor = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "#ff00ff"),
                    PaletteJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    RequiredFeaturesJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    ForbiddenChangesJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    Frozen = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetProfiles_ArtAssets_CanonicalAssetId",
                        column: x => x.CanonicalAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetProfiles_ArtAssets_StyleAssetId",
                        column: x => x.StyleAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssetProfiles_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetAnimationCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetAnimationJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GenerationBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OutputAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CandidateIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "generated"),
                    RawQaStatus = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    RawQaSummaryJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetAnimationCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetAnimationCandidates_ArtAssets_OutputAssetId",
                        column: x => x.OutputAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssetAnimationCandidates_GenerationBatches_GenerationBatchId",
                        column: x => x.GenerationBatchId,
                        principalTable: "GenerationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssetAnimationCandidates_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetAnimationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuideAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DiagnosticGuideAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OutputSpriteSheetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SelectedCandidateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "planned"),
                    AnimationKind = table.Column<string>(type: "TEXT", nullable: false),
                    Strategy = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "hybrid"),
                    PromptSummary = table.Column<string>(type: "TEXT", nullable: false),
                    RecommendedAction = table.Column<string>(type: "TEXT", nullable: false),
                    MaxGenerationRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    GenerationRoundsUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxRepairAttemptsPerFrame = table.Column<int>(type: "INTEGER", nullable: false),
                    AnimationSpecJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    LayoutSpecJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    RawQaSummaryJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    FrameQaSummaryJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    MotionQaSummaryJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    FrameStatusesJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetAnimationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetAnimationJobs_ArtAssets_DiagnosticGuideAssetId",
                        column: x => x.DiagnosticGuideAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssetAnimationJobs_ArtAssets_GuideAssetId",
                        column: x => x.GuideAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssetAnimationJobs_AssetAnimationCandidates_SelectedCandidateId",
                        column: x => x.SelectedCandidateId,
                        principalTable: "AssetAnimationCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssetAnimationJobs_AssetProfiles_AssetProfileId",
                        column: x => x.AssetProfileId,
                        principalTable: "AssetProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetAnimationJobs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetAnimationJobs_SpriteSheetDefinitions_OutputSpriteSheetId",
                        column: x => x.OutputSpriteSheetId,
                        principalTable: "SpriteSheetDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AssetAnimationFrameAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetAnimationJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetAnimationCandidateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FrameIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AttemptKind = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "mark"),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: false),
                    RepairHistoryJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    SourceAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceX = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceY = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetAnimationFrameAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetAnimationFrameAttempts_ArtAssets_SourceAssetId",
                        column: x => x.SourceAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssetAnimationFrameAttempts_AssetAnimationCandidates_AssetAnimationCandidateId",
                        column: x => x.AssetAnimationCandidateId,
                        principalTable: "AssetAnimationCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssetAnimationFrameAttempts_AssetAnimationJobs_AssetAnimationJobId",
                        column: x => x.AssetAnimationJobId,
                        principalTable: "AssetAnimationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetAnimationFrameAttempts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpriteSheetFrameRecords_SourceAnimationCandidateId",
                table: "SpriteSheetFrameRecords",
                column: "SourceAnimationCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteSheetFrameRecords_SourceAnimationJobId",
                table: "SpriteSheetFrameRecords",
                column: "SourceAnimationJobId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationCandidates_AssetAnimationJobId",
                table: "AssetAnimationCandidates",
                column: "AssetAnimationJobId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationCandidates_GenerationBatchId",
                table: "AssetAnimationCandidates",
                column: "GenerationBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationCandidates_OutputAssetId",
                table: "AssetAnimationCandidates",
                column: "OutputAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationCandidates_ProjectId_AssetAnimationJobId_CandidateIndex",
                table: "AssetAnimationCandidates",
                columns: new[] { "ProjectId", "AssetAnimationJobId", "CandidateIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationFrameAttempts_AssetAnimationCandidateId",
                table: "AssetAnimationFrameAttempts",
                column: "AssetAnimationCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationFrameAttempts_AssetAnimationJobId",
                table: "AssetAnimationFrameAttempts",
                column: "AssetAnimationJobId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationFrameAttempts_ProjectId_AssetAnimationJobId_FrameIndex_AttemptNumber",
                table: "AssetAnimationFrameAttempts",
                columns: new[] { "ProjectId", "AssetAnimationJobId", "FrameIndex", "AttemptNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationFrameAttempts_SourceAssetId",
                table: "AssetAnimationFrameAttempts",
                column: "SourceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationJobs_AssetProfileId",
                table: "AssetAnimationJobs",
                column: "AssetProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationJobs_DiagnosticGuideAssetId",
                table: "AssetAnimationJobs",
                column: "DiagnosticGuideAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationJobs_GuideAssetId",
                table: "AssetAnimationJobs",
                column: "GuideAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationJobs_OutputSpriteSheetId",
                table: "AssetAnimationJobs",
                column: "OutputSpriteSheetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationJobs_ProjectId_CreatedAt",
                table: "AssetAnimationJobs",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAnimationJobs_SelectedCandidateId",
                table: "AssetAnimationJobs",
                column: "SelectedCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetProfiles_CanonicalAssetId",
                table: "AssetProfiles",
                column: "CanonicalAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetProfiles_ProjectId_CreatedAt",
                table: "AssetProfiles",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetProfiles_StyleAssetId",
                table: "AssetProfiles",
                column: "StyleAssetId");

            migrationBuilder.AddForeignKey(
                name: "FK_SpriteSheetFrameRecords_AssetAnimationCandidates_SourceAnimationCandidateId",
                table: "SpriteSheetFrameRecords",
                column: "SourceAnimationCandidateId",
                principalTable: "AssetAnimationCandidates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SpriteSheetFrameRecords_AssetAnimationJobs_SourceAnimationJobId",
                table: "SpriteSheetFrameRecords",
                column: "SourceAnimationJobId",
                principalTable: "AssetAnimationJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AssetAnimationCandidates_AssetAnimationJobs_AssetAnimationJobId",
                table: "AssetAnimationCandidates",
                column: "AssetAnimationJobId",
                principalTable: "AssetAnimationJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SpriteSheetFrameRecords_AssetAnimationCandidates_SourceAnimationCandidateId",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_SpriteSheetFrameRecords_AssetAnimationJobs_SourceAnimationJobId",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_AssetAnimationCandidates_AssetAnimationJobs_AssetAnimationJobId",
                table: "AssetAnimationCandidates");

            migrationBuilder.DropTable(
                name: "AssetAnimationFrameAttempts");

            migrationBuilder.DropTable(
                name: "AssetAnimationJobs");

            migrationBuilder.DropTable(
                name: "AssetAnimationCandidates");

            migrationBuilder.DropTable(
                name: "AssetProfiles");

            migrationBuilder.DropIndex(
                name: "IX_SpriteSheetFrameRecords_SourceAnimationCandidateId",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropIndex(
                name: "IX_SpriteSheetFrameRecords_SourceAnimationJobId",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "AppliedScale",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "FootContactsJson",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "IsKeyframe",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "PivotX",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "PivotY",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "PoseName",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "QaStatus",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "RepairHistoryJson",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "RootOffsetX",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "RootOffsetY",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "SourceAnimationCandidateId",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "SourceAnimationJobId",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "TranslationX",
                table: "SpriteSheetFrameRecords");

            migrationBuilder.DropColumn(
                name: "TranslationY",
                table: "SpriteSheetFrameRecords");
        }
    }
}
