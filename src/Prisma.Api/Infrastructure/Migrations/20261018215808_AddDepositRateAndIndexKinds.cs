using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositRateAndIndexKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fixed_income_deposits_index_kind",
                table: "fixed_income_deposits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fixed_income_deposits_percent",
                table: "fixed_income_deposits");

            migrationBuilder.RenameColumn(
                name: "index_percent_e4",
                table: "fixed_income_deposits",
                newName: "rate_e4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixed_income_deposits_index_kind",
                table: "fixed_income_deposits",
                sql: "index_kind IN ('Cdi', 'CdiPlus', 'Prefixed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixed_income_deposits_rate",
                table: "fixed_income_deposits",
                sql: "(index_kind = 'Cdi' AND rate_e4 BETWEEN 100 AND 5000000) OR (index_kind IN ('CdiPlus', 'Prefixed') AND rate_e4 BETWEEN 100 AND 1000000)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fixed_income_deposits_index_kind",
                table: "fixed_income_deposits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fixed_income_deposits_rate",
                table: "fixed_income_deposits");

            migrationBuilder.RenameColumn(
                name: "rate_e4",
                table: "fixed_income_deposits",
                newName: "index_percent_e4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixed_income_deposits_index_kind",
                table: "fixed_income_deposits",
                sql: "index_kind IN ('Cdi')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixed_income_deposits_percent",
                table: "fixed_income_deposits",
                sql: "index_percent_e4 BETWEEN 100 AND 5000000");
        }
    }
}
