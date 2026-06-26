using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260626000000_SpriteEditSessions")]
    public partial class SpriteEditSessions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpriteEditSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    ModalOpen = table.Column<bool>(type: "INTEGER", nullable: false),
                    TargetKind = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "source"),
                    TargetSourceAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetFrameSetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetFrameId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SelectedCandidateAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SelectedOutputIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    PreviewOverlayActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    CropJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    CandidateAssetIdsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    OutputStatesJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        name: "FK_SpriteEditSessions_Frames_TargetFrameId",
                        column: x => x.TargetFrameId,
                        principalTable: "Frames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SpriteEditSessions_FrameSets_TargetFrameSetId",
                        column: x => x.TargetFrameSetId,
                        principalTable: "FrameSets",
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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SpriteEditSessions");
        }
    }
}
