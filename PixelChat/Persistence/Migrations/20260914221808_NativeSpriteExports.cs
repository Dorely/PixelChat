using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeSpriteExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Empty pre-native sets acquired a blank frame during materialization. Project its identity too.
            migrationBuilder.Sql("""
                INSERT INTO Frames (Id, ProjectId, FrameSetId, Name, "Index", SourceX, SourceY, SourceWidth, SourceHeight,
                    LogicalWidth, LogicalHeight, ContentOffsetX, ContentOffsetY, DurationMs, HideFromOnionSkin, ShapeJson, IsDeleted, CreatedAt, UpdatedAt)
                SELECT upper(json_extract(f.value, '$.id')), s.ProjectId, s.Id, json_extract(f.value, '$.name'), cast(f.key as integer), 0, 0, 0, 0,
                    json_extract(f.value, '$.width'), json_extract(f.value, '$.height'), 0, 0, json_extract(f.value, '$.durationMs'), 0, '[]', 0, s.CreatedAt, s.UpdatedAt
                FROM FrameSets s, json_each(s.DocumentJson, '$.frames') f
                WHERE NOT EXISTS (SELECT 1 FROM Frames existing WHERE upper(existing.Id) = upper(json_extract(f.value, '$.id')));
                """);
            migrationBuilder.CreateTable(
                name: "SpriteExports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FrameSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    SpecificationJson = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteExports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpriteExports_FrameSets_FrameSetId",
                        column: x => x.FrameSetId,
                        principalTable: "FrameSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpriteExports_FrameSetId_Revision",
                table: "SpriteExports",
                columns: new[] { "FrameSetId", "Revision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpriteExports");
        }
    }
}
