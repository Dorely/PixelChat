using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistRecipeBulkQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "RecipePromptSnapshot", table: "GenerationBatches", type: "TEXT", nullable: false, defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AnimationNameSnapshot",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AnimationPromptSnapshot",
                table: "GenerationBatches",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsBulk",
                table: "GenerationBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GenerationOutputs",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OutputIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    PromptIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputWithinPrompt = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StateJson = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerationOutputs", x => new { x.BatchId, x.OutputIndex });
                    table.ForeignKey(
                        name: "FK_GenerationOutputs_GenerationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "GenerationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GenerationPrompts",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerationPrompts", x => new { x.BatchId, x.Index });
                    table.ForeignKey(
                        name: "FK_GenerationPrompts_GenerationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "GenerationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GenerationReferences",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerationReferences", x => new { x.BatchId, x.Index });
                    table.ForeignKey(
                        name: "FK_GenerationReferences_GenerationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "GenerationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationOutputs_BatchId_Status_OutputIndex",
                table: "GenerationOutputs",
                columns: new[] { "BatchId", "Status", "OutputIndex" });
            // Expand the old arrays once, preserving each output's original index, error, and asset link.
            migrationBuilder.Sql("""
                INSERT INTO GenerationPrompts (BatchId, "Index", Prompt, Count, OutputName)
                SELECT b.Id, CAST(p.key AS INTEGER), json_extract(p.value,'$.prompt'),
                    json_extract(p.value,'$.count'), json_extract(p.value,'$.outputName')
                FROM GenerationBatches b, json_each(b.PromptSpecsJson) p;

                WITH RECURSIVE slots(BatchId, OutputIndex, Total) AS (
                    SELECT Id, 0, Count FROM GenerationBatches WHERE Count > 0
                    UNION ALL SELECT BatchId, OutputIndex + 1, Total FROM slots WHERE OutputIndex + 1 < Total
                ), assets AS (
                    SELECT Id, SourceBatchId, COALESCE(json_extract(SourceMetadataJson,'$.outputIndex'),
                        ROW_NUMBER() OVER (PARTITION BY SourceBatchId ORDER BY CreatedAt, Id)-1) AS OutputIndex
                    FROM ArtAssets WHERE SourceBatchId IS NOT NULL
                )
                INSERT INTO GenerationOutputs (BatchId, OutputIndex, PromptIndex, OutputWithinPrompt, Status, AssetId, StateJson, ErrorJson)
                SELECT slot.BatchId, slot.OutputIndex,
                    COALESCE((SELECT p."Index" FROM GenerationPrompts p WHERE p.BatchId=slot.BatchId
                        AND slot.OutputIndex < (SELECT SUM(q.Count) FROM GenerationPrompts q WHERE q.BatchId=p.BatchId AND q."Index"<=p."Index")
                        ORDER BY p."Index" LIMIT 1),0),
                    slot.OutputIndex-COALESCE((SELECT SUM(q.Count) FROM GenerationPrompts q WHERE q.BatchId=slot.BatchId
                        AND q."Index" < COALESCE((SELECT p."Index" FROM GenerationPrompts p WHERE p.BatchId=slot.BatchId
                            AND slot.OutputIndex < (SELECT SUM(r.Count) FROM GenerationPrompts r WHERE r.BatchId=p.BatchId AND r."Index"<=p."Index")
                            ORDER BY p."Index" LIMIT 1),0)),0),
                    CASE WHEN a.Id IS NOT NULL THEN 'Succeeded' ELSE COALESCE(json_extract(st.value,'$.status'),CASE WHEN er.value IS NOT NULL THEN 'Failed' ELSE 'Queued' END) END,
                    a.Id,
                    COALESCE(st.value,json_object('outputIndex',slot.OutputIndex,'status',CASE WHEN a.Id IS NOT NULL THEN 'Succeeded' WHEN er.value IS NOT NULL THEN 'Failed' ELSE 'Queued' END)),
                    CASE WHEN a.Id IS NULL THEN er.value ELSE NULL END
                FROM slots slot JOIN GenerationBatches b ON b.Id=slot.BatchId
                LEFT JOIN json_each(b.OutputStatesJson) st ON json_extract(st.value,'$.outputIndex')=slot.OutputIndex
                LEFT JOIN json_each(b.OutputErrorsJson) er ON json_extract(er.value,'$.outputIndex')=slot.OutputIndex
                LEFT JOIN assets a ON a.SourceBatchId=slot.BatchId AND a.OutputIndex=slot.OutputIndex;

                INSERT INTO GenerationReferences (BatchId, "Index", SourceAssetId, Label, FileName, ContentType, Data)
                SELECT b.Id, CAST(ref.key AS INTEGER), a.Id, a.Label, a.FileName, a.ContentType, a.Data
                FROM GenerationBatches b, json_each(b.InputAssetIdsJson) ref
                JOIN ArtAssets a ON lower(a.Id)=lower(ref.value)
                WHERE b.EditSourceData IS NULL OR CAST(ref.key AS INTEGER)>0;

                UPDATE GenerationBatches SET
                    RecipePromptSnapshot=COALESCE((SELECT Prompt FROM PromptRecipeVersions WHERE RecipeId=GenerationBatches.PromptRecipeId AND Version=GenerationBatches.PromptRecipeVersion),
                        (SELECT Prompt FROM PromptRecipes WHERE Id=GenerationBatches.PromptRecipeId),''),
                    AnimationPromptSnapshot=COALESCE((SELECT Prompt FROM AnimationRecipeVersions WHERE AnimationRecipeId=GenerationBatches.AnimationRecipeId AND Version=GenerationBatches.AnimationRecipeVersion),
                        (SELECT Prompt FROM AnimationRecipes WHERE Id=GenerationBatches.AnimationRecipeId),''),
                    AnimationNameSnapshot=COALESCE((SELECT Name FROM AnimationRecipeVersions WHERE AnimationRecipeId=GenerationBatches.AnimationRecipeId AND Version=GenerationBatches.AnimationRecipeVersion),
                        (SELECT Name FROM AnimationRecipes WHERE Id=GenerationBatches.AnimationRecipeId),'');
                """);
            migrationBuilder.DropColumn(name: "OutputStatesJson", table: "GenerationBatches");
            migrationBuilder.DropColumn(name: "OutputErrorsJson", table: "GenerationBatches");
            migrationBuilder.DropColumn(name: "PromptSpecsJson", table: "GenerationBatches");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Restore a database backup to downgrade the normalized generation queue without losing its snapshots.");
    }
}
