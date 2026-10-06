using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "asset_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payout_kind",
                table: "transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_asset_id",
                table: "transactions",
                column: "asset_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_payout",
                table: "transactions",
                sql: "(asset_id IS NULL AND payout_kind IS NULL) OR (asset_id IS NOT NULL AND payout_kind IS NOT NULL AND type = 'Income')");

            migrationBuilder.AddForeignKey(
                name: "fk_transactions_assets_asset_id",
                table: "transactions",
                column: "asset_id",
                principalTable: "assets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transactions_assets_asset_id",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_asset_id",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_payout",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "asset_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "payout_kind",
                table: "transactions");
        }
    }
}
