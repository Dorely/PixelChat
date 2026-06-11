using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromptRecipeVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromptRecipeVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecipeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
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
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    ChangeSummary = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptRecipeVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptRecipeVersions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromptRecipeVersions_PromptRecipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "PromptRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO PromptRecipeVersions (
                    Id,
                    ProjectId,
                    RecipeId,
                    Version,
                    Name,
                    AssetType,
                    PromptTemplate,
                    StyleRulesJson,
                    AvoidRulesJson,
                    ExampleAssetIdsJson,
                    PreferredProvider,
                    PreferredModel,
                    PreferredSize,
                    ExportDefaultsJson,
                    Notes,
                    Source,
                    ChangeSummary,
                    CreatedAt
                )
                SELECT
                    lower(hex(randomblob(4))) || '-' ||
                    lower(hex(randomblob(2))) || '-' ||
                    '4' || substr(lower(hex(randomblob(2))), 2) || '-' ||
                    substr('89ab', abs(random()) % 4 + 1, 1) || substr(lower(hex(randomblob(2))), 2) || '-' ||
                    lower(hex(randomblob(6))),
                    ProjectId,
                    Id,
                    1,
                    Name,
                    AssetType,
                    PromptTemplate,
                    StyleRulesJson,
                    AvoidRulesJson,
                    ExampleAssetIdsJson,
                    PreferredProvider,
                    PreferredModel,
                    PreferredSize,
                    ExportDefaultsJson,
                    Notes,
                    'system',
                    'Initial version from existing recipe.',
                    CreatedAt
                FROM PromptRecipes;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PromptRecipeVersions_ProjectId_RecipeId",
                table: "PromptRecipeVersions",
                columns: new[] { "ProjectId", "RecipeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PromptRecipeVersions_RecipeId_Version",
                table: "PromptRecipeVersions",
                columns: new[] { "RecipeId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromptRecipeVersions");
        }
    }
}
