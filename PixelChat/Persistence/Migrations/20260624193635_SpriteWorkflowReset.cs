using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpriteWorkflowReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "running"),
                    Actor = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "system"),
                    WorkflowKind = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryArtifactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PrimaryArtifactKind = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityRuns_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnimationRecipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AnimationKind = table.Column<string>(type: "TEXT", nullable: false),
                    Facing = table.Column<string>(type: "TEXT", nullable: false),
                    FrameCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FrameOrderJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    Fps = table.Column<int>(type: "INTEGER", nullable: false),
                    Loop = table.Column<bool>(type: "INTEGER", nullable: false),
                    GuideAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpectedFrameBoxesJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    AnchorStrategy = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "recipe-defined"),
                    PromptScaffold = table.Column<string>(type: "TEXT", nullable: false),
                    ExportDefaultsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryExampleSpriteSheetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimationRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnimationRecipes_ArtAssets_GuideAssetId",
                        column: x => x.GuideAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AnimationRecipes_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnimationRecipes_SpriteSheetDefinitions_PrimaryExampleSpriteSheetId",
                        column: x => x.PrimaryExampleSpriteSheetId,
                        principalTable: "SpriteSheetDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ActivityArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    RefId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityArtifacts_ActivityRuns_ActivityRunId",
                        column: x => x.ActivityRunId,
                        principalTable: "ActivityRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityArtifacts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActivitySteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "completed"),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivitySteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivitySteps_ActivityRuns_ActivityRunId",
                        column: x => x.ActivityRunId,
                        principalTable: "ActivityRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivitySteps_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnimationRecipeVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnimationRecipeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AnimationKind = table.Column<string>(type: "TEXT", nullable: false),
                    Facing = table.Column<string>(type: "TEXT", nullable: false),
                    FrameCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FrameOrderJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    Fps = table.Column<int>(type: "INTEGER", nullable: false),
                    Loop = table.Column<bool>(type: "INTEGER", nullable: false),
                    GuideAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpectedFrameBoxesJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    AnchorStrategy = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "recipe-defined"),
                    PromptScaffold = table.Column<string>(type: "TEXT", nullable: false),
                    ExportDefaultsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryExampleSpriteSheetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    ChangeSummary = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimationRecipeVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnimationRecipeVersions_AnimationRecipes_AnimationRecipeId",
                        column: x => x.AnimationRecipeId,
                        principalTable: "AnimationRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnimationRecipeVersions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityArtifacts_ActivityRunId_SortOrder",
                table: "ActivityArtifacts",
                columns: new[] { "ActivityRunId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityArtifacts_ProjectId_Kind_RefId",
                table: "ActivityArtifacts",
                columns: new[] { "ProjectId", "Kind", "RefId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRuns_ProjectId_UpdatedAt",
                table: "ActivityRuns",
                columns: new[] { "ProjectId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRuns_ProjectId_WorkflowKind",
                table: "ActivityRuns",
                columns: new[] { "ProjectId", "WorkflowKind" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySteps_ActivityRunId_SortOrder",
                table: "ActivitySteps",
                columns: new[] { "ActivityRunId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySteps_ProjectId",
                table: "ActivitySteps",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AnimationRecipes_GuideAssetId",
                table: "AnimationRecipes",
                column: "GuideAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AnimationRecipes_PrimaryExampleSpriteSheetId",
                table: "AnimationRecipes",
                column: "PrimaryExampleSpriteSheetId");

            migrationBuilder.CreateIndex(
                name: "IX_AnimationRecipes_ProjectId_Name",
                table: "AnimationRecipes",
                columns: new[] { "ProjectId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_AnimationRecipeVersions_AnimationRecipeId_Version",
                table: "AnimationRecipeVersions",
                columns: new[] { "AnimationRecipeId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnimationRecipeVersions_ProjectId_AnimationRecipeId",
                table: "AnimationRecipeVersions",
                columns: new[] { "ProjectId", "AnimationRecipeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityArtifacts");

            migrationBuilder.DropTable(
                name: "ActivitySteps");

            migrationBuilder.DropTable(
                name: "AnimationRecipeVersions");

            migrationBuilder.DropTable(
                name: "ActivityRuns");

            migrationBuilder.DropTable(
                name: "AnimationRecipes");
        }
    }
}
