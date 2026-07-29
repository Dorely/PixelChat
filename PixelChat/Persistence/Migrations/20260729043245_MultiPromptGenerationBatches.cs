using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiPromptGenerationBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PromptSpecsJson",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.Sql(
                """
                UPDATE "GenerationBatches"
                SET "PromptSpecsJson" = json_array(
                    json_object(
                        'prompt', "Prompt",
                        'count', "Count",
                        'outputName', NULL));
                """);

            migrationBuilder.DropColumn(
                name: "Prompt",
                table: "GenerationBatches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Prompt",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE "GenerationBatches"
                SET "Prompt" = COALESCE(
                    json_extract("PromptSpecsJson", '$[0].prompt'),
                    '');
                """);

            migrationBuilder.DropColumn(
                name: "PromptSpecsJson",
                table: "GenerationBatches");
        }
    }
}
