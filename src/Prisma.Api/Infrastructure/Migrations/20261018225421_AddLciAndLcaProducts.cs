using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLciAndLcaProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fixed_income_deposits_product_kind",
                table: "fixed_income_deposits");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixed_income_deposits_product_kind",
                table: "fixed_income_deposits",
                sql: "product_kind IN ('Cdb', 'Lci', 'Lca')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fixed_income_deposits_product_kind",
                table: "fixed_income_deposits");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixed_income_deposits_product_kind",
                table: "fixed_income_deposits",
                sql: "product_kind IN ('Cdb')");
        }
    }
}
