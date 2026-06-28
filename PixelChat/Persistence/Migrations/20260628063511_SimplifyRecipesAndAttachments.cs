using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyRecipesAndAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AnimationRecipes_ArtAssets_GuideAssetId",
                table: "AnimationRecipes");

            migrationBuilder.DropForeignKey(
                name: "FK_AnimationRecipes_SpriteSheetDefinitions_PrimaryExampleSpriteSheetId",
                table: "AnimationRecipes");

            migrationBuilder.DropIndex(
                name: "IX_AnimationRecipes_GuideAssetId",
                table: "AnimationRecipes");

            migrationBuilder.DropIndex(
                name: "IX_AnimationRecipes_PrimaryExampleSpriteSheetId",
                table: "AnimationRecipes");

            migrationBuilder.Sql("""
                CREATE TABLE __RecipeAssetAttachmentBackfill
                (
                    Id TEXT NOT NULL,
                    ProjectId TEXT NOT NULL,
                    PromptRecipeId TEXT NULL,
                    AnimationRecipeId TEXT NULL,
                    AssetId TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    Notes TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO __RecipeAssetAttachmentBackfill
                    (Id, ProjectId, PromptRecipeId, AnimationRecipeId, AssetId, Role, SortOrder, Notes, CreatedAt, UpdatedAt)
                SELECT
                    lower(hex(randomblob(16))),
                    r.ProjectId,
                    r.Id,
                    NULL,
                    r.ExampleAssetId,
                    'example',
                    0,
                    '',
                    r.CreatedAt,
                    r.UpdatedAt
                FROM PromptRecipes r
                INNER JOIN ArtAssets a ON a.Id = r.ExampleAssetId AND a.ProjectId = r.ProjectId
                WHERE r.ExampleAssetId IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                INSERT INTO __RecipeAssetAttachmentBackfill
                    (Id, ProjectId, PromptRecipeId, AnimationRecipeId, AssetId, Role, SortOrder, Notes, CreatedAt, UpdatedAt)
                SELECT
                    lower(hex(randomblob(16))),
                    r.ProjectId,
                    NULL,
                    r.Id,
                    r.GuideAssetId,
                    'guide',
                    0,
                    '',
                    r.CreatedAt,
                    r.UpdatedAt
                FROM AnimationRecipes r
                INNER JOIN ArtAssets a ON a.Id = r.GuideAssetId AND a.ProjectId = r.ProjectId
                WHERE r.GuideAssetId IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                INSERT INTO __RecipeAssetAttachmentBackfill
                    (Id, ProjectId, PromptRecipeId, AnimationRecipeId, AssetId, Role, SortOrder, Notes, CreatedAt, UpdatedAt)
                SELECT
                    lower(hex(randomblob(16))),
                    r.ProjectId,
                    NULL,
                    r.Id,
                    COALESCE(s.OutputAssetId, s.SourceAssetId),
                    'example',
                    1,
                    '',
                    r.CreatedAt,
                    r.UpdatedAt
                FROM AnimationRecipes r
                INNER JOIN SpriteSheetDefinitions s ON s.Id = r.PrimaryExampleSpriteSheetId AND s.ProjectId = r.ProjectId
                INNER JOIN ArtAssets a ON a.Id = COALESCE(s.OutputAssetId, s.SourceAssetId) AND a.ProjectId = r.ProjectId
                WHERE r.PrimaryExampleSpriteSheetId IS NOT NULL
                    AND COALESCE(s.OutputAssetId, s.SourceAssetId) IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "AssetType",
                table: "PromptRecipeVersions");

            migrationBuilder.DropColumn(
                name: "AvoidRulesJson",
                table: "PromptRecipeVersions");

            migrationBuilder.DropColumn(
                name: "ExampleAssetId",
                table: "PromptRecipeVersions");

            migrationBuilder.DropColumn(
                name: "ExportDefaultsJson",
                table: "PromptRecipeVersions");

            migrationBuilder.DropColumn(
                name: "PreferredModel",
                table: "PromptRecipeVersions");

            migrationBuilder.DropColumn(
                name: "PreferredProvider",
                table: "PromptRecipeVersions");

            migrationBuilder.DropColumn(
                name: "PreferredSize",
                table: "PromptRecipeVersions");

            migrationBuilder.DropColumn(
                name: "StyleRulesJson",
                table: "PromptRecipeVersions");

            migrationBuilder.DropColumn(
                name: "AssetType",
                table: "PromptRecipes");

            migrationBuilder.DropColumn(
                name: "AvoidRulesJson",
                table: "PromptRecipes");

            migrationBuilder.DropColumn(
                name: "ExampleAssetId",
                table: "PromptRecipes");

            migrationBuilder.DropColumn(
                name: "ExportDefaultsJson",
                table: "PromptRecipes");

            migrationBuilder.DropColumn(
                name: "PreferredModel",
                table: "PromptRecipes");

            migrationBuilder.DropColumn(
                name: "PreferredProvider",
                table: "PromptRecipes");

            migrationBuilder.DropColumn(
                name: "PreferredSize",
                table: "PromptRecipes");

            migrationBuilder.DropColumn(
                name: "StyleRulesJson",
                table: "PromptRecipes");

            migrationBuilder.DropColumn(
                name: "AnchorStrategy",
                table: "AnimationRecipeVersions");

            migrationBuilder.DropColumn(
                name: "AnimationKind",
                table: "AnimationRecipeVersions");

            migrationBuilder.DropColumn(
                name: "ExpectedFrameBoxesJson",
                table: "AnimationRecipeVersions");

            migrationBuilder.DropColumn(
                name: "ExportDefaultsJson",
                table: "AnimationRecipeVersions");

            migrationBuilder.DropColumn(
                name: "Facing",
                table: "AnimationRecipeVersions");

            migrationBuilder.DropColumn(
                name: "Fps",
                table: "AnimationRecipeVersions");

            migrationBuilder.DropColumn(
                name: "FrameCount",
                table: "AnimationRecipeVersions");

            migrationBuilder.DropColumn(
                name: "FrameOrderJson",
                table: "AnimationRecipeVersions");

            migrationBuilder.DropColumn(
                name: "GuideAssetId",
                table: "AnimationRecipeVersions");

            migrationBuilder.DropColumn(
                name: "Loop",
                table: "AnimationRecipeVersions");

            migrationBuilder.DropColumn(
                name: "PrimaryExampleSpriteSheetId",
                table: "AnimationRecipeVersions");

            migrationBuilder.DropColumn(
                name: "AnchorStrategy",
                table: "AnimationRecipes");

            migrationBuilder.DropColumn(
                name: "AnimationKind",
                table: "AnimationRecipes");

            migrationBuilder.DropColumn(
                name: "ExpectedFrameBoxesJson",
                table: "AnimationRecipes");

            migrationBuilder.DropColumn(
                name: "ExportDefaultsJson",
                table: "AnimationRecipes");

            migrationBuilder.DropColumn(
                name: "Facing",
                table: "AnimationRecipes");

            migrationBuilder.DropColumn(
                name: "Fps",
                table: "AnimationRecipes");

            migrationBuilder.DropColumn(
                name: "FrameCount",
                table: "AnimationRecipes");

            migrationBuilder.DropColumn(
                name: "FrameOrderJson",
                table: "AnimationRecipes");

            migrationBuilder.DropColumn(
                name: "GuideAssetId",
                table: "AnimationRecipes");

            migrationBuilder.DropColumn(
                name: "Loop",
                table: "AnimationRecipes");

            migrationBuilder.DropColumn(
                name: "PrimaryExampleSpriteSheetId",
                table: "AnimationRecipes");

            migrationBuilder.RenameColumn(
                name: "PromptTemplate",
                table: "PromptRecipeVersions",
                newName: "Prompt");

            migrationBuilder.RenameColumn(
                name: "PromptTemplate",
                table: "PromptRecipes",
                newName: "Prompt");

            migrationBuilder.RenameColumn(
                name: "PromptScaffold",
                table: "AnimationRecipeVersions",
                newName: "Prompt");

            migrationBuilder.RenameColumn(
                name: "PromptScaffold",
                table: "AnimationRecipes",
                newName: "Prompt");

            migrationBuilder.CreateTable(
                name: "RecipeAssetAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromptRecipeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AnimationRecipeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "example"),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeAssetAttachments", x => x.Id);
                    table.CheckConstraint("CK_RecipeAssetAttachments_OneRecipeParent", "((PromptRecipeId IS NOT NULL AND AnimationRecipeId IS NULL) OR (PromptRecipeId IS NULL AND AnimationRecipeId IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_RecipeAssetAttachments_AnimationRecipes_AnimationRecipeId",
                        column: x => x.AnimationRecipeId,
                        principalTable: "AnimationRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeAssetAttachments_ArtAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeAssetAttachments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeAssetAttachments_PromptRecipes_PromptRecipeId",
                        column: x => x.PromptRecipeId,
                        principalTable: "PromptRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO RecipeAssetAttachments
                    (Id, ProjectId, PromptRecipeId, AnimationRecipeId, AssetId, Role, SortOrder, Notes, CreatedAt, UpdatedAt)
                SELECT
                    Id,
                    ProjectId,
                    PromptRecipeId,
                    AnimationRecipeId,
                    AssetId,
                    Role,
                    SortOrder,
                    Notes,
                    CreatedAt,
                    UpdatedAt
                FROM __RecipeAssetAttachmentBackfill;
                """);

            migrationBuilder.Sql("DROP TABLE __RecipeAssetAttachmentBackfill;");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeAssetAttachments_AnimationRecipeId",
                table: "RecipeAssetAttachments",
                column: "AnimationRecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeAssetAttachments_AssetId",
                table: "RecipeAssetAttachments",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeAssetAttachments_ProjectId_AnimationRecipeId_SortOrder",
                table: "RecipeAssetAttachments",
                columns: new[] { "ProjectId", "AnimationRecipeId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeAssetAttachments_ProjectId_PromptRecipeId_SortOrder",
                table: "RecipeAssetAttachments",
                columns: new[] { "ProjectId", "PromptRecipeId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeAssetAttachments_PromptRecipeId",
                table: "RecipeAssetAttachments",
                column: "PromptRecipeId");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecipeAssetAttachments");

            migrationBuilder.RenameColumn(
                name: "Prompt",
                table: "PromptRecipeVersions",
                newName: "PromptTemplate");

            migrationBuilder.RenameColumn(
                name: "Prompt",
                table: "PromptRecipes",
                newName: "PromptTemplate");

            migrationBuilder.RenameColumn(
                name: "Prompt",
                table: "AnimationRecipeVersions",
                newName: "PromptScaffold");

            migrationBuilder.RenameColumn(
                name: "Prompt",
                table: "AnimationRecipes",
                newName: "PromptScaffold");

            migrationBuilder.AddColumn<string>(
                name: "AssetType",
                table: "PromptRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AvoidRulesJson",
                table: "PromptRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ExampleAssetId",
                table: "PromptRecipeVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExportDefaultsJson",
                table: "PromptRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PreferredModel",
                table: "PromptRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PreferredProvider",
                table: "PromptRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PreferredSize",
                table: "PromptRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StyleRulesJson",
                table: "PromptRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AssetType",
                table: "PromptRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AvoidRulesJson",
                table: "PromptRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ExampleAssetId",
                table: "PromptRecipes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExportDefaultsJson",
                table: "PromptRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PreferredModel",
                table: "PromptRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PreferredProvider",
                table: "PromptRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PreferredSize",
                table: "PromptRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StyleRulesJson",
                table: "PromptRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AnchorStrategy",
                table: "AnimationRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "recipe-defined");

            migrationBuilder.AddColumn<string>(
                name: "AnimationKind",
                table: "AnimationRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExpectedFrameBoxesJson",
                table: "AnimationRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "ExportDefaultsJson",
                table: "AnimationRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "Facing",
                table: "AnimationRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Fps",
                table: "AnimationRecipeVersions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FrameCount",
                table: "AnimationRecipeVersions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FrameOrderJson",
                table: "AnimationRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "GuideAssetId",
                table: "AnimationRecipeVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Loop",
                table: "AnimationRecipeVersions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PrimaryExampleSpriteSheetId",
                table: "AnimationRecipeVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnchorStrategy",
                table: "AnimationRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "recipe-defined");

            migrationBuilder.AddColumn<string>(
                name: "AnimationKind",
                table: "AnimationRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExpectedFrameBoxesJson",
                table: "AnimationRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "ExportDefaultsJson",
                table: "AnimationRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "Facing",
                table: "AnimationRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Fps",
                table: "AnimationRecipes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FrameCount",
                table: "AnimationRecipes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FrameOrderJson",
                table: "AnimationRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "GuideAssetId",
                table: "AnimationRecipes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Loop",
                table: "AnimationRecipes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PrimaryExampleSpriteSheetId",
                table: "AnimationRecipes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnimationRecipes_GuideAssetId",
                table: "AnimationRecipes",
                column: "GuideAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AnimationRecipes_PrimaryExampleSpriteSheetId",
                table: "AnimationRecipes",
                column: "PrimaryExampleSpriteSheetId");

            migrationBuilder.AddForeignKey(
                name: "FK_AnimationRecipes_ArtAssets_GuideAssetId",
                table: "AnimationRecipes",
                column: "GuideAssetId",
                principalTable: "ArtAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AnimationRecipes_SpriteSheetDefinitions_PrimaryExampleSpriteSheetId",
                table: "AnimationRecipes",
                column: "PrimaryExampleSpriteSheetId",
                principalTable: "SpriteSheetDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
