using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositFunding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "transfer_pair_id",
                table: "fixed_income_deposits",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_fixed_income_deposits_user_id_transfer_pair_id",
                table: "fixed_income_deposits",
                columns: new[] { "user_id", "transfer_pair_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_fixed_income_deposits_user_id_transfer_pair_id",
                table: "fixed_income_deposits");

            migrationBuilder.DropColumn(
                name: "transfer_pair_id",
                table: "fixed_income_deposits");
        }
    }
}
