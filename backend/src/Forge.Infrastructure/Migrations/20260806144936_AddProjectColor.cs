using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "projects",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Backfill existing rows with a pastel color from the same fixed palette as
            // ProjectColorPalette.Colors (Forge.Domain) - kept in literal sync here since
            // migrations can't reference application code. Round-robin by creation order
            // (not a single flat default) so existing projects stay visually
            // distinguishable from each other, same as POST /projects' own assignment.
            migrationBuilder.Sql(@"
                WITH palette (color, idx) AS (
                    VALUES
                        ('#FFD1DC', 0),
                        ('#FFE4B5', 1),
                        ('#FFFACD', 2),
                        ('#D4F1D4', 3),
                        ('#C1E7FF', 4),
                        ('#D9C6F0', 5),
                        ('#FFCCCB', 6),
                        ('#C6F0EB', 7),
                        ('#F0D9E8', 8),
                        ('#E8DCC8', 9)
                ),
                ordered AS (
                    SELECT id, row_number() OVER (ORDER BY created_at, id) - 1 AS rn
                    FROM projects
                )
                UPDATE projects SET color = palette.color
                FROM ordered
                JOIN palette ON palette.idx = ordered.rn % 10
                WHERE projects.id = ordered.id;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "color",
                table: "projects");
        }
    }
}
