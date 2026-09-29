using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "occurrence_date",
                table: "transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recurrence_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    generated_through = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurrences", x => x.id);
                    table.CheckConstraint("ck_recurrences_amount_cents_positive", "amount_cents > 0");
                    table.CheckConstraint("ck_recurrences_type", "type IN ('Expense', 'Income')");
                    table.ForeignKey(
                        name: "fk_recurrences_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurrences_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurrences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recurrence_pendings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    occurrence_date = table.Column<DateOnly>(type: "date", nullable: false),
                    statement_reference = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurrence_pendings", x => x.id);
                    table.CheckConstraint("ck_recurrence_pendings_amount_cents_positive", "amount_cents > 0");
                    table.ForeignKey(
                        name: "fk_recurrence_pendings_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurrence_pendings_recurrences_recurrence_id",
                        column: x => x.recurrence_id,
                        principalTable: "recurrences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurrence_pendings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_transactions_recurrence_occurrence",
                table: "transactions",
                columns: new[] { "recurrence_id", "occurrence_date" },
                unique: true,
                filter: "recurrence_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_recurrence_occurrence",
                table: "transactions",
                sql: "(recurrence_id IS NULL) = (occurrence_date IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_recurrence_pendings_account_id",
                table: "recurrence_pendings",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurrence_pendings_user_id",
                table: "recurrence_pendings",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_recurrence_pendings_occurrence",
                table: "recurrence_pendings",
                columns: new[] { "recurrence_id", "occurrence_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recurrences_account_id",
                table: "recurrences",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurrences_category_id",
                table: "recurrences",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurrences_user_id",
                table: "recurrences",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_transactions_recurrences_recurrence_id",
                table: "transactions",
                column: "recurrence_id",
                principalTable: "recurrences",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transactions_recurrences_recurrence_id",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "recurrence_pendings");

            migrationBuilder.DropTable(
                name: "recurrences");

            migrationBuilder.DropIndex(
                name: "ux_transactions_recurrence_occurrence",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_recurrence_occurrence",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "occurrence_date",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "recurrence_id",
                table: "transactions");
        }
    }
}
