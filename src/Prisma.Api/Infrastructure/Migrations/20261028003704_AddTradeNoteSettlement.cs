using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeNoteSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "trade_note_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "settlement_account_id",
                table: "trade_notes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_transactions_trade_note",
                table: "transactions",
                column: "trade_note_id",
                unique: true,
                filter: "trade_note_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_trade_settlement",
                table: "transactions",
                sql: "(type = 'TradeSettlement') = (trade_note_id IS NOT NULL AND transfer_direction IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_trade_notes_settlement_account_id",
                table: "trade_notes",
                column: "settlement_account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_trade_notes_accounts_settlement_account_id",
                table: "trade_notes",
                column: "settlement_account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_trade_notes_accounts_settlement_account_id",
                table: "trade_notes");

            migrationBuilder.DropIndex(
                name: "ux_transactions_trade_note",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_trade_settlement",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_trade_notes_settlement_account_id",
                table: "trade_notes");

            migrationBuilder.DropColumn(
                name: "trade_note_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "settlement_account_id",
                table: "trade_notes");
        }
    }
}
