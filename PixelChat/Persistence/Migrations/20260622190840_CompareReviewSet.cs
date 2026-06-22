using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelChat.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompareReviewSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompareReviewSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompareReviewSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompareReviewSets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompareReviewSetItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompareReviewSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    RefId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompareReviewSetItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompareReviewSetItems_CompareReviewSets_CompareReviewSetId",
                        column: x => x.CompareReviewSetId,
                        principalTable: "CompareReviewSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompareReviewSetItems_CompareReviewSetId_Kind_RefId",
                table: "CompareReviewSetItems",
                columns: new[] { "CompareReviewSetId", "Kind", "RefId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompareReviewSetItems_CompareReviewSetId_SortOrder",
                table: "CompareReviewSetItems",
                columns: new[] { "CompareReviewSetId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CompareReviewSets_ProjectId",
                table: "CompareReviewSets",
                column: "ProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompareReviewSetItems");

            migrationBuilder.DropTable(
                name: "CompareReviewSets");
        }
    }
}
