using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStatementPinned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "statement_pinned",
                table: "transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_statement_pinned",
                table: "transactions",
                sql: "NOT statement_pinned OR (statement_id IS NOT NULL AND type = 'Expense')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_statement_pinned",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "statement_pinned",
                table: "transactions");
        }
    }
}
