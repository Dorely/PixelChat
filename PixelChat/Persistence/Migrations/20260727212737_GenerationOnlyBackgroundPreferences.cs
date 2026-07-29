using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GenerationOnlyBackgroundPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundPreference",
                table: "PromptRecipeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "current");

            migrationBuilder.AddColumn<string>(
                name: "BackgroundPreference",
                table: "PromptRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "current");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundPreference",
                table: "PromptRecipeVersions");

            migrationBuilder.DropColumn(
                name: "BackgroundPreference",
                table: "PromptRecipes");
        }
    }
}
