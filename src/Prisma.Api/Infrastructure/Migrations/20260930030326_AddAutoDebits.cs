using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoDebits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "amount_estimated",
                table: "transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "amount_varies",
                table: "recurrences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "recurrences",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Regular"); // as séries que já existem são comuns

            migrationBuilder.CreateIndex(
                name: "ix_transactions_to_confirm",
                table: "transactions",
                columns: new[] { "user_id", "purchase_date" },
                filter: "amount_estimated AND deleted_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_amount_estimated",
                table: "transactions",
                sql: "NOT amount_estimated OR recurrence_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recurrences_amount_varies",
                table: "recurrences",
                sql: "NOT amount_varies OR kind = 'AutoDebit'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recurrences_auto_debit",
                table: "recurrences",
                sql: "kind <> 'AutoDebit' OR (type = 'Expense' AND method = 'Debit' AND frequency = 'Monthly')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transactions_to_confirm",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_amount_estimated",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recurrences_amount_varies",
                table: "recurrences");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recurrences_auto_debit",
                table: "recurrences");

            migrationBuilder.DropColumn(
                name: "amount_estimated",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "amount_varies",
                table: "recurrences");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "recurrences");
        }
    }
}
