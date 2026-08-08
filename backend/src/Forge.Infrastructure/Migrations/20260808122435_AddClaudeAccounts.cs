using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClaudeAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "claude_account_id",
                table: "runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "claude_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_claude_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_claude_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_runs_claude_account",
                table: "runs",
                column: "claude_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_claude_accounts_user",
                table: "claude_accounts",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_runs_claude_accounts_claude_account_id",
                table: "runs",
                column: "claude_account_id",
                principalTable: "claude_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_runs_claude_accounts_claude_account_id",
                table: "runs");

            migrationBuilder.DropTable(
                name: "claude_accounts");

            migrationBuilder.DropIndex(
                name: "ix_runs_claude_account",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "claude_account_id",
                table: "runs");
        }
    }
}
