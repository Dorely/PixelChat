using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260712000000_AssetReviewWorkflow")]
    public partial class AssetReviewWorkflow : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "ArtAssets",
                type: "TEXT",
                nullable: false,
                defaultValue: "Kept");

            migrationBuilder.AddColumn<string>(
                name: "ReviewCompletedBy",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewCompletedAt",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetReviewDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Decision = table.Column<string>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetReviewDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetReviewDecisions_ArtAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetReviewDecisions_GenerationBatches_SourceBatchId",
                        column: x => x.SourceBatchId,
                        principalTable: "GenerationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssetReviewDecisions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtAssets_ProjectId_ReviewStatus_CreatedAt",
                table: "ArtAssets",
                columns: new[] { "ProjectId", "ReviewStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetReviewDecisions_AssetId",
                table: "AssetReviewDecisions",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetReviewDecisions_ProjectId_AssetId_CreatedAt",
                table: "AssetReviewDecisions",
                columns: new[] { "ProjectId", "AssetId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetReviewDecisions_ProjectId_SourceBatchId_CreatedAt",
                table: "AssetReviewDecisions",
                columns: new[] { "ProjectId", "SourceBatchId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetReviewDecisions_SourceBatchId",
                table: "AssetReviewDecisions",
                column: "SourceBatchId");

            migrationBuilder.Sql("DELETE FROM CompareReviewSetItems WHERE Kind IN ('ArtRecipe', 'AnimationRecipe');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AssetReviewDecisions");

            migrationBuilder.DropIndex(
                name: "IX_ArtAssets_ProjectId_ReviewStatus_CreatedAt",
                table: "ArtAssets");

            migrationBuilder.DropColumn(name: "ReviewStatus", table: "ArtAssets");
            migrationBuilder.DropColumn(name: "ReviewCompletedBy", table: "GenerationBatches");
            migrationBuilder.DropColumn(name: "ReviewCompletedAt", table: "GenerationBatches");
        }
    }
}
