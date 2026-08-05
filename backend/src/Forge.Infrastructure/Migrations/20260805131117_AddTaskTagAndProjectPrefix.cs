using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTagAndProjectPrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "number",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "next_task_number",
                table: "projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "prefix",
                table: "projects",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Backfill existing rows before the unique indexes below can be created -
            // every project starts with the same "" prefix and every task the same 0
            // number, which collides the instant there's more than one row.
            migrationBuilder.Sql(@"
                UPDATE projects SET prefix = upper(left(regexp_replace(name, '[^a-zA-Z0-9]', '', 'g'), 6)) || left(md5(id::text), 4)
                WHERE prefix = '';

                WITH numbered AS (
                    SELECT id, row_number() OVER (PARTITION BY project_id ORDER BY created_at) AS rn
                    FROM tasks
                )
                UPDATE tasks SET number = numbered.rn
                FROM numbered WHERE tasks.id = numbered.id;

                UPDATE projects SET next_task_number = coalesce(
                    (SELECT max(number) + 1 FROM tasks WHERE tasks.project_id = projects.id), 1);
            ");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_number",
                table: "tasks",
                columns: new[] { "project_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_prefix",
                table: "projects",
                column: "prefix",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tasks_project_id_number",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_projects_prefix",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "number",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "next_task_number",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "prefix",
                table: "projects");
        }
    }
}
