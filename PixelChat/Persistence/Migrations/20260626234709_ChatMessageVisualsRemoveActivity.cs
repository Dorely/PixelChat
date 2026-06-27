using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatMessageVisualsRemoveActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Projects SET ActiveWorkspaceMode = 'Generate' WHERE ActiveWorkspaceMode = 'Runs';");

            migrationBuilder.DropTable(
                name: "ActivityArtifacts");

            migrationBuilder.DropTable(
                name: "ActivitySteps");

            migrationBuilder.DropTable(
                name: "ActivityRuns");

            migrationBuilder.CreateTable(
                name: "AssistantMessageVisuals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssistantMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ToolCallId = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Caption = table.Column<string>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", nullable: false),
                    SourceRefId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ThumbnailData = table.Column<byte[]>(type: "BLOB", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantMessageVisuals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantMessageVisuals_AssistantMessages_AssistantMessageId",
                        column: x => x.AssistantMessageId,
                        principalTable: "AssistantMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMessageVisuals_AssistantMessageId_SortOrder",
                table: "AssistantMessageVisuals",
                columns: new[] { "AssistantMessageId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMessageVisuals_SourceKind_SourceRefId",
                table: "AssistantMessageVisuals",
                columns: new[] { "SourceKind", "SourceRefId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMessageVisuals_ToolCallId",
                table: "AssistantMessageVisuals",
                column: "ToolCallId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistantMessageVisuals");

            migrationBuilder.CreateTable(
                name: "ActivityRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "system"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PrimaryArtifactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PrimaryArtifactKind = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "running"),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WorkflowKind = table.Column<string>(type: "TEXT", nullable: false)
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
                name: "ActivityArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    RefId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
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
                    ActivityRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "completed"),
                    Title = table.Column<string>(type: "TEXT", nullable: false)
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
        }
    }
}
