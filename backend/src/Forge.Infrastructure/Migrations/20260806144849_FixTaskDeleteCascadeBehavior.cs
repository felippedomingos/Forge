using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixTaskDeleteCascadeBehavior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_events_tasks_task_id",
                table: "events");

            migrationBuilder.DropForeignKey(
                name: "fk_tasks_worktrees_worktree_id",
                table: "tasks");

            migrationBuilder.AddForeignKey(
                name: "fk_events_tasks_task_id",
                table: "events",
                column: "task_id",
                principalTable: "tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_worktrees_worktree_id",
                table: "tasks",
                column: "worktree_id",
                principalTable: "worktrees",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_events_tasks_task_id",
                table: "events");

            migrationBuilder.DropForeignKey(
                name: "fk_tasks_worktrees_worktree_id",
                table: "tasks");

            migrationBuilder.AddForeignKey(
                name: "fk_events_tasks_task_id",
                table: "events",
                column: "task_id",
                principalTable: "tasks",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_worktrees_worktree_id",
                table: "tasks",
                column: "worktree_id",
                principalTable: "worktrees",
                principalColumn: "id");
        }
    }
}
