using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRedemptionFunding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "income_transaction_id",
                table: "fixed_income_redemptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "transfer_pair_id",
                table: "fixed_income_redemptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_fixed_income_redemptions_user_id_transfer_pair_id",
                table: "fixed_income_redemptions",
                columns: new[] { "user_id", "transfer_pair_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_fixed_income_redemptions_user_id_transfer_pair_id",
                table: "fixed_income_redemptions");

            migrationBuilder.DropColumn(
                name: "income_transaction_id",
                table: "fixed_income_redemptions");

            migrationBuilder.DropColumn(
                name: "transfer_pair_id",
                table: "fixed_income_redemptions");
        }
    }
}
