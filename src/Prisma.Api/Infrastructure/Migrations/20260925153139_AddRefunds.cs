using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "refunded_transaction_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_refunded_transaction_id",
                table: "transactions",
                column: "refunded_transaction_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_refund_link",
                table: "transactions",
                sql: "refunded_transaction_id IS NULL OR type = 'Refund'");

            migrationBuilder.AddForeignKey(
                name: "fk_transactions_transactions_refunded_transaction_id",
                table: "transactions",
                column: "refunded_transaction_id",
                principalTable: "transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transactions_transactions_refunded_transaction_id",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_refunded_transaction_id",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_refund_link",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "refunded_transaction_id",
                table: "transactions");
        }
    }
}
