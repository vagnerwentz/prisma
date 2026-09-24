using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowSingleInstallmentPurchase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_installment_purchases_installment_count",
                table: "installment_purchases");

            migrationBuilder.AddCheckConstraint(
                name: "ck_installment_purchases_installment_count",
                table: "installment_purchases",
                sql: "installment_count BETWEEN 1 AND 24");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_installment_purchases_installment_count",
                table: "installment_purchases");

            migrationBuilder.AddCheckConstraint(
                name: "ck_installment_purchases_installment_count",
                table: "installment_purchases",
                sql: "installment_count BETWEEN 2 AND 24");
        }
    }
}
