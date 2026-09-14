using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpriteInspectionArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpriteInspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FrameSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheKey = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    BitmapHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpriteInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpriteInspections_FrameSets_FrameSetId",
                        column: x => x.FrameSetId,
                        principalTable: "FrameSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SpriteInspections_SpriteBitmaps_BitmapHash",
                        column: x => x.BitmapHash,
                        principalTable: "SpriteBitmaps",
                        principalColumn: "Hash",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpriteInspections_BitmapHash",
                table: "SpriteInspections",
                column: "BitmapHash");

            migrationBuilder.CreateIndex(
                name: "IX_SpriteInspections_FrameSetId_Revision_CacheKey",
                table: "SpriteInspections",
                columns: new[] { "FrameSetId", "Revision", "CacheKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpriteInspections");
        }
    }
}
