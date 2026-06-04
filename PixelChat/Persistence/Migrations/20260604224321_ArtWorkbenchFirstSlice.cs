using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArtWorkbenchFirstSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ToolCallId",
                table: "AssistantMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolCallsJson",
                table: "AssistantMessages",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ToolName",
                table: "AssistantMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "AssistantConversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ActiveWorkspaceMode = table.Column<string>(type: "TEXT", nullable: false),
                    ActiveAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActiveBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatContextAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    RefId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatContextAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatContextAttachments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptRecipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AssetType = table.Column<string>(type: "TEXT", nullable: false),
                    PromptTemplate = table.Column<string>(type: "TEXT", nullable: false),
                    StyleRulesJson = table.Column<string>(type: "TEXT", nullable: false),
                    AvoidRulesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExampleAssetIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    PreferredProvider = table.Column<string>(type: "TEXT", nullable: false),
                    PreferredModel = table.Column<string>(type: "TEXT", nullable: false),
                    PreferredSize = table.Column<string>(type: "TEXT", nullable: false),
                    ExportDefaultsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptRecipes_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GenerationBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    MainlineModel = table.Column<string>(type: "TEXT", nullable: false),
                    ImageModel = table.Column<string>(type: "TEXT", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    NegativePrompt = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<string>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    InputAssetIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    InputMaskIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ParentBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PromptRecipeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: false),
                    AgentSummary = table.Column<string>(type: "TEXT", nullable: false),
                    RawProviderResponseJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerationBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GenerationBatches_GenerationBatches_ParentBatchId",
                        column: x => x.ParentBatchId,
                        principalTable: "GenerationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GenerationBatches_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GenerationBatches_PromptRecipes_PromptRecipeId",
                        column: x => x.PromptRecipeId,
                        principalTable: "PromptRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ArtAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ThumbnailData = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    ParentAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourcePromptRecipeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRejected = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsReference = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    SourceMetadataJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArtAssets_ArtAssets_ParentAssetId",
                        column: x => x.ParentAssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ArtAssets_GenerationBatches_SourceBatchId",
                        column: x => x.SourceBatchId,
                        principalTable: "GenerationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ArtAssets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArtAssets_PromptRecipes_SourcePromptRecipeId",
                        column: x => x.SourcePromptRecipeId,
                        principalTable: "PromptRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ImageMasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageMasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageMasks_ArtAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImageMasks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConversations_ProjectId",
                table: "AssistantConversations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtAssets_ParentAssetId",
                table: "ArtAssets",
                column: "ParentAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtAssets_ProjectId_CreatedAt",
                table: "ArtAssets",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ArtAssets_SourceBatchId",
                table: "ArtAssets",
                column: "SourceBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtAssets_SourcePromptRecipeId",
                table: "ArtAssets",
                column: "SourcePromptRecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatContextAttachments_ProjectId_SortOrder",
                table: "ChatContextAttachments",
                columns: new[] { "ProjectId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationBatches_ParentBatchId",
                table: "GenerationBatches",
                column: "ParentBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationBatches_ProjectId_CreatedAt",
                table: "GenerationBatches",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationBatches_PromptRecipeId",
                table: "GenerationBatches",
                column: "PromptRecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageMasks_AssetId",
                table: "ImageMasks",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageMasks_ProjectId_CreatedAt",
                table: "ImageMasks",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PromptRecipes_ProjectId_Name",
                table: "PromptRecipes",
                columns: new[] { "ProjectId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_AssistantConversations_Projects_ProjectId",
                table: "AssistantConversations",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssistantConversations_Projects_ProjectId",
                table: "AssistantConversations");

            migrationBuilder.DropTable(
                name: "ChatContextAttachments");

            migrationBuilder.DropTable(
                name: "ImageMasks");

            migrationBuilder.DropTable(
                name: "ArtAssets");

            migrationBuilder.DropTable(
                name: "GenerationBatches");

            migrationBuilder.DropTable(
                name: "PromptRecipes");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_AssistantConversations_ProjectId",
                table: "AssistantConversations");

            migrationBuilder.DropColumn(
                name: "ToolCallId",
                table: "AssistantMessages");

            migrationBuilder.DropColumn(
                name: "ToolCallsJson",
                table: "AssistantMessages");

            migrationBuilder.DropColumn(
                name: "ToolName",
                table: "AssistantMessages");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "AssistantConversations");
        }
    }
}
