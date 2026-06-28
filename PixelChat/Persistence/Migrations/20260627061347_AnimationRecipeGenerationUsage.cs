using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnimationRecipeGenerationUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AnimationRecipeId",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnimationRecipeVersion",
                table: "GenerationBatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceAnimationRecipeId",
                table: "ArtAssets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceAnimationRecipeVersion",
                table: "ArtAssets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GenerationBatches_AnimationRecipeId",
                table: "GenerationBatches",
                column: "AnimationRecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtAssets_SourceAnimationRecipeId",
                table: "ArtAssets",
                column: "SourceAnimationRecipeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ArtAssets_AnimationRecipes_SourceAnimationRecipeId",
                table: "ArtAssets",
                column: "SourceAnimationRecipeId",
                principalTable: "AnimationRecipes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_GenerationBatches_AnimationRecipes_AnimationRecipeId",
                table: "GenerationBatches",
                column: "AnimationRecipeId",
                principalTable: "AnimationRecipes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ArtAssets_AnimationRecipes_SourceAnimationRecipeId",
                table: "ArtAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_GenerationBatches_AnimationRecipes_AnimationRecipeId",
                table: "GenerationBatches");

            migrationBuilder.DropIndex(
                name: "IX_GenerationBatches_AnimationRecipeId",
                table: "GenerationBatches");

            migrationBuilder.DropIndex(
                name: "IX_ArtAssets_SourceAnimationRecipeId",
                table: "ArtAssets");

            migrationBuilder.DropColumn(
                name: "AnimationRecipeId",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "AnimationRecipeVersion",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "SourceAnimationRecipeId",
                table: "ArtAssets");

            migrationBuilder.DropColumn(
                name: "SourceAnimationRecipeVersion",
                table: "ArtAssets");
        }
    }
}
