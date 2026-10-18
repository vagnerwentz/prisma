using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSavingsGoalsAndDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "savings_goals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "citext", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    target_cents = table.Column<long>(type: "bigint", nullable: true),
                    default_index_percent_e4 = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_savings_goals", x => x.id);
                    table.CheckConstraint("ck_savings_goals_default_percent", "default_index_percent_e4 IS NULL OR default_index_percent_e4 BETWEEN 100 AND 5000000");
                    table.CheckConstraint("ck_savings_goals_name", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_savings_goals_target", "target_cents IS NULL OR target_cents > 0");
                    table.ForeignKey(
                        name: "fk_savings_goals_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fixed_income_deposits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deposit_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    product_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    index_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    index_percent_e4 = table.Column<long>(type: "bigint", nullable: false),
                    maturity_date = table.Column<DateOnly>(type: "date", nullable: true),
                    liquidity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fixed_income_deposits", x => x.id);
                    table.CheckConstraint("ck_fixed_income_deposits_amount", "amount_cents >= 1 AND amount_cents <= 100000000000");
                    table.CheckConstraint("ck_fixed_income_deposits_index_kind", "index_kind IN ('Cdi')");
                    table.CheckConstraint("ck_fixed_income_deposits_liquidity", "liquidity IN ('Unknown', 'Daily', 'AtMaturity')");
                    table.CheckConstraint("ck_fixed_income_deposits_maturity", "maturity_date IS NULL OR maturity_date > deposit_date");
                    table.CheckConstraint("ck_fixed_income_deposits_percent", "index_percent_e4 BETWEEN 100 AND 5000000");
                    table.CheckConstraint("ck_fixed_income_deposits_product_kind", "product_kind IN ('Cdb')");
                    table.ForeignKey(
                        name: "fk_fixed_income_deposits_savings_goals_goal_id",
                        column: x => x.goal_id,
                        principalTable: "savings_goals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fixed_income_deposits_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fixed_income_deposits_goal_id",
                table: "fixed_income_deposits",
                column: "goal_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_income_deposits_user_id_goal_id_deposit_date",
                table: "fixed_income_deposits",
                columns: new[] { "user_id", "goal_id", "deposit_date" });

            migrationBuilder.CreateIndex(
                name: "ux_fixed_income_deposits_user_request",
                table: "fixed_income_deposits",
                columns: new[] { "user_id", "client_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_savings_goals_user_name",
                table: "savings_goals",
                columns: new[] { "user_id", "name" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fixed_income_deposits");

            migrationBuilder.DropTable(
                name: "savings_goals");
        }
    }
}
