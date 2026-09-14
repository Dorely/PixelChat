using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeSpriteJobsAndAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "SpriteLogicalSourceData",
                table: "GenerationBatches",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SpriteProviderMaskData",
                table: "GenerationBatches",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpriteTargetJson",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "SpriteAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FrameSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpriteAssessments_FrameSets_FrameSetId",
                        column: x => x.FrameSetId,
                        principalTable: "FrameSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpriteAssessments_FrameSetId_Revision",
                table: "SpriteAssessments",
                columns: new[] { "FrameSetId", "Revision" });
            // Preserve meaningful old edit provenance while retiring its UI/session ownership.
            migrationBuilder.Sql("""
                INSERT INTO SpriteAssessments (Id, FrameSetId, Revision, Kind, ResultJson, CreatedAt)
                SELECT s.Id, f.Id, f.Revision, 'imported-session-provenance', json_object('Id', s."Id", 'TargetKind', s."TargetKind", 'TargetSourceAssetId', s."TargetSourceAssetId", 'TargetFrameSetId', s."TargetFrameSetId", 'TargetFrameId', s."TargetFrameId", 'BatchId', s."BatchId", 'MaskId', s."MaskId", 'SelectedCandidateAssetId', s."SelectedCandidateAssetId", 'SelectedOutputIndex', s."SelectedOutputIndex", 'Prompt', s."Prompt", 'Count', s."Count", 'CanvasOptionsJson', s."CanvasOptionsJson", 'CanvasPreparationTransformJson', s."CanvasPreparationTransformJson", 'CropJson', s."CropJson", 'CandidateAssetIdsJson', s."CandidateAssetIdsJson", 'OutputStatesJson', s."OutputStatesJson", 'Status', s."Status", 'CreatedAt', s."CreatedAt", 'UpdatedAt', s."UpdatedAt"), s.UpdatedAt
                FROM SpriteEditSessions s JOIN FrameSets f ON f.Id = s.TargetFrameSetId;
                UPDATE ArtAssets SET SourceMetadataJson = json_set(CASE WHEN json_valid(SourceMetadataJson) THEN SourceMetadataJson ELSE '{}' END,
                    '$.importedEditSessions', json((SELECT json_group_array(json_object('Id', s."Id", 'TargetKind', s."TargetKind", 'TargetSourceAssetId', s."TargetSourceAssetId", 'TargetFrameSetId', s."TargetFrameSetId", 'TargetFrameId', s."TargetFrameId", 'BatchId', s."BatchId", 'MaskId', s."MaskId", 'SelectedCandidateAssetId', s."SelectedCandidateAssetId", 'SelectedOutputIndex', s."SelectedOutputIndex", 'Prompt', s."Prompt", 'Count', s."Count", 'CanvasOptionsJson', s."CanvasOptionsJson", 'CanvasPreparationTransformJson', s."CanvasPreparationTransformJson", 'CropJson', s."CropJson", 'CandidateAssetIdsJson', s."CandidateAssetIdsJson", 'OutputStatesJson', s."OutputStatesJson", 'Status', s."Status", 'CreatedAt', s."CreatedAt", 'UpdatedAt', s."UpdatedAt")) FROM SpriteEditSessions s WHERE s.TargetSourceAssetId = ArtAssets.Id)))
                WHERE EXISTS (SELECT 1 FROM SpriteEditSessions s WHERE s.TargetSourceAssetId = ArtAssets.Id);
                """);
            migrationBuilder.DropTable(name: "SpriteEditSessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpriteAssessments");

            migrationBuilder.DropColumn(
                name: "SpriteLogicalSourceData",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "SpriteProviderMaskData",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "SpriteTargetJson",
                table: "GenerationBatches");

            migrationBuilder.CreateTable(
                name: "SpriteEditSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Background = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "preserve"),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CandidateAssetIdsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    CanvasOptionsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    CanvasPreparationExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CanvasPreparationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CanvasPreparationTransformJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CropJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    MaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModalOpen = table.Column<bool>(type: "INTEGER", nullable: false),
                    OutputStatesJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    PreviewOverlayActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    SelectedCandidateAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SelectedOutputIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    TargetFrameId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetFrameSetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetKind = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "source"),
                    TargetSourceAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteEditSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpriteEditSessions_ArtAssets_SelectedCandidateAssetId",
                        column: x => x.SelectedCandidateAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SpriteEditSessions_ArtAssets_TargetSourceAssetId",
                        column: x => x.TargetSourceAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SpriteEditSessions_FrameSets_TargetFrameSetId",
                        column: x => x.TargetFrameSetId,
                        principalTable: "FrameSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SpriteEditSessions_Frames_TargetFrameId",
                        column: x => x.TargetFrameId,
                        principalTable: "Frames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SpriteEditSessions_GenerationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "GenerationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SpriteEditSessions_ImageMasks_MaskId",
                        column: x => x.MaskId,
                        principalTable: "ImageMasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SpriteEditSessions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpriteEditSessions_BatchId",
                table: "SpriteEditSessions",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteEditSessions_MaskId",
                table: "SpriteEditSessions",
                column: "MaskId");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteEditSessions_ProjectId",
                table: "SpriteEditSessions",
                column: "ProjectId",
                unique: true,
                filter: "Status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteEditSessions_SelectedCandidateAssetId",
                table: "SpriteEditSessions",
                column: "SelectedCandidateAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteEditSessions_TargetFrameId",
                table: "SpriteEditSessions",
                column: "TargetFrameId");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteEditSessions_TargetFrameSetId",
                table: "SpriteEditSessions",
                column: "TargetFrameSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteEditSessions_TargetSourceAssetId",
                table: "SpriteEditSessions",
                column: "TargetSourceAssetId");
        }
    }
}
