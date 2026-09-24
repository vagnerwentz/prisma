using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    initial_balance_cents = table.Column<long>(type: "bigint", nullable: false),
                    closing_day = table.Column<int>(type: "integer", nullable: true),
                    due_day = table.Column<int>(type: "integer", nullable: true),
                    credit_limit_cents = table.Column<long>(type: "bigint", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_card_fields", "(type = 'CreditCard' AND closing_day IS NOT NULL AND due_day IS NOT NULL) OR (type <> 'CreditCard' AND closing_day IS NULL AND due_day IS NULL AND credit_limit_cents IS NULL)");
                    table.CheckConstraint("ck_accounts_closing_day", "closing_day BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_accounts_credit_limit_cents", "credit_limit_cents >= 0");
                    table.CheckConstraint("ck_accounts_due_day", "due_day BETWEEN 1 AND 31");
                    table.ForeignKey(
                        name: "fk_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_user_id",
                table: "accounts",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
