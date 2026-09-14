using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpenAIModelsAndNativeTransparency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Before this migration, this spelling meant removable magenta, not native alpha.
            migrationBuilder.Sql("UPDATE GenerationBatches SET Background = 'removable' WHERE lower(trim(Background)) = 'transparent';");
            migrationBuilder.Sql("UPDATE PromptRecipes SET BackgroundPreference = 'removable' WHERE lower(trim(BackgroundPreference)) = 'transparent';");
            migrationBuilder.Sql("UPDATE PromptRecipeVersions SET BackgroundPreference = 'removable' WHERE lower(trim(BackgroundPreference)) = 'transparent';");
            migrationBuilder.Sql("UPDATE LlmProviders SET ThinkingMode = 'medium' WHERE Name = 'openai-account' AND ModelId IN ('gpt-5.6-sol', 'gpt-5.6-terra', 'gpt-5.6-luna', 'gpt-6-astra') AND (ThinkingMode IS NULL OR ThinkingMode NOT IN ('low', 'medium', 'high', 'xhigh', 'max'));");
            migrationBuilder.AddColumn<string>(
                name: "Background",
                table: "SpriteEditSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "preserve");

            migrationBuilder.AddColumn<string>(
                name: "OutputFormat",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: false,
                defaultValue: "png");

            migrationBuilder.AddColumn<string>(
                name: "Quality",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: false,
                defaultValue: "auto");

            migrationBuilder.CreateTable(
                name: "WorkbenchPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImageModel = table.Column<string>(type: "TEXT", nullable: false),
                    ImageQuality = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkbenchPreferences", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkbenchPreferences");

            migrationBuilder.DropColumn(
                name: "Background",
                table: "SpriteEditSessions");

            migrationBuilder.DropColumn(
                name: "OutputFormat",
                table: "GenerationBatches");

            migrationBuilder.DropColumn(
                name: "Quality",
                table: "GenerationBatches");
        }
    }
}
