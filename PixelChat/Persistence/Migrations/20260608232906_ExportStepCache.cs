using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExportStepCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExportStepCaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceImageSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    StepIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentImageSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    OutputImageSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", nullable: false),
                    OptionsHash = table.Column<string>(type: "TEXT", nullable: false),
                    ModelName = table.Column<string>(type: "TEXT", nullable: false),
                    ActualBackend = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportStepCaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExportStepCaches_ArtAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "ArtAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExportStepCaches_AssetId",
                table: "ExportStepCaches",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ExportStepCaches_OutputImageSha256",
                table: "ExportStepCaches",
                column: "OutputImageSha256");

            migrationBuilder.CreateIndex(
                name: "IX_ExportStepCaches_ProjectId_AssetId_SourceImageSha256",
                table: "ExportStepCaches",
                columns: new[] { "ProjectId", "AssetId", "SourceImageSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportStepCaches_ProjectId_AssetId_SourceImageSha256_StepIndex",
                table: "ExportStepCaches",
                columns: new[] { "ProjectId", "AssetId", "SourceImageSha256", "StepIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExportStepCaches");
        }
    }
}
