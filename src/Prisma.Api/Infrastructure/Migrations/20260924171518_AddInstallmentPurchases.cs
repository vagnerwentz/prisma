using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallmentPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installment_purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    total_amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    installment_count = table.Column<int>(type: "integer", nullable: false),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installment_purchases", x => x.id);
                    table.CheckConstraint("ck_installment_purchases_cent_per_installment", "total_amount_cents >= installment_count");
                    table.CheckConstraint("ck_installment_purchases_installment_count", "installment_count BETWEEN 2 AND 24");
                    table.ForeignKey(
                        name: "fk_installment_purchases_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_installment_purchases_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_installment_purchase_id",
                table: "transactions",
                column: "installment_purchase_id");

            migrationBuilder.CreateIndex(
                name: "ix_installment_purchases_account_id",
                table: "installment_purchases",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_installment_purchases_user_id",
                table: "installment_purchases",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_transactions_installment_purchases_installment_purchase_id",
                table: "transactions",
                column: "installment_purchase_id",
                principalTable: "installment_purchases",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transactions_installment_purchases_installment_purchase_id",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "installment_purchases");

            migrationBuilder.DropIndex(
                name: "ix_transactions_installment_purchase_id",
                table: "transactions");
        }
    }
}
