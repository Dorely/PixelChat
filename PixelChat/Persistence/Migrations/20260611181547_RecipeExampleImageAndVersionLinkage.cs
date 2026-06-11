using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecipeExampleImageAndVersionLinkage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExampleAssetId",
                table: "PromptRecipeVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExampleAssetId",
                table: "PromptRecipes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromptRecipeVersion",
                table: "GenerationBatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourcePromptRecipeVersion",
                table: "ArtAssets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE PromptRecipes
                SET ExampleAssetId = json_extract(ExampleAssetIdsJson, '$[0]')
                WHERE ExampleAssetIdsJson IS NOT NULL
                    AND json_valid(ExampleAssetIdsJson)
                    AND json_type(ExampleAssetIdsJson, '$[0]') = 'text'
                    AND length(json_extract(ExampleAssetIdsJson, '$[0]')) > 0;

                UPDATE PromptRecipeVersions
                SET ExampleAssetId = json_extract(ExampleAssetIdsJson, '$[0]')
                WHERE ExampleAssetIdsJson IS NOT NULL
                    AND json_valid(ExampleAssetIdsJson)
                    AND json_type(ExampleAssetIdsJson, '$[0]') = 'text'
                    AND length(json_extract(ExampleAssetIdsJson, '$[0]')) > 0;
                """);

            migrationBuilder.DropColumn(
                name: "ExampleAssetIdsJson",
                table: "PromptRecipeVersions");

            migrationBuilder.DropColumn(
                name: "ExampleAssetIdsJson",
                table: "PromptRecipes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExampleAssetIdsJson",
                table: "PromptRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "ExampleAssetIdsJson",
                table: "PromptRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.Sql(
                """
                UPDATE PromptRecipes
                SET ExampleAssetIdsJson = json_array(ExampleAssetId)
                WHERE ExampleAssetId IS NOT NULL;

                UPDATE PromptRecipeVersions
                SET ExampleAssetIdsJson = json_array(ExampleAssetId)
                WHERE ExampleAssetId IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "ExampleAssetId",
                table: "PromptRecipeVersions");

            migrationBuilder.DropColumn(
                name: "ExampleAssetId",
                table: "PromptRecipes");

            migrationBuilder.DropColumn(
                name: "PromptRecipeVersion",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "SourcePromptRecipeVersion",
                table: "ArtAssets");
        }
    }
}
