using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "transfer_direction",
                table: "transactions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_transfer_pair_id",
                table: "transactions",
                column: "transfer_pair_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_transfer_legs",
                table: "transactions",
                sql: "(type = 'Transfer') = (transfer_pair_id IS NOT NULL AND transfer_direction IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transactions_transfer_pair_id",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_transfer_legs",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "transfer_direction",
                table: "transactions");
        }
    }
}
