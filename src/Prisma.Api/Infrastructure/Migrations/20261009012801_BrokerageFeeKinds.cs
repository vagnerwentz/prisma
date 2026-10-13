using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BrokerageFeeKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Corretagem por tipo (docs/investimentos.md, I.7b3). As notas existentes ficam com o mesmo rateio: a
            // marcação ligada vira todos os tipos; desligada, só ação. Converte antes de tirar a coluna antiga.
            migrationBuilder.AddColumn<string[]>(
                name: "brokerage_fee_kinds",
                table: "trade_notes",
                type: "text[]",
                nullable: false,
                defaultValue: new[] { "Stock" });

            migrationBuilder.Sql(
                """
                UPDATE trade_notes
                SET brokerage_fee_kinds = ARRAY['Stock', 'Fii', 'Etf', 'FiInfra', 'FiAgro', 'Fip', 'Fidc', 'OtherFund', 'Bdr', 'Unknown']::text[]
                WHERE brokerage_fee_on_all_kinds;
                """);

            migrationBuilder.DropColumn(
                name: "brokerage_fee_on_all_kinds",
                table: "trade_notes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_notes_brokerage_fee_kinds",
                table: "trade_notes",
                sql: "brokerage_fee_kinds <@ ARRAY['Stock', 'Fii', 'Etf', 'FiInfra', 'FiAgro', 'Fip', 'Fidc', 'OtherFund', 'Bdr', 'Unknown']::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_notes_brokerage_fee_kinds",
                table: "trade_notes");

            migrationBuilder.AddColumn<bool>(
                name: "brokerage_fee_on_all_kinds",
                table: "trade_notes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Volta perdendo detalhe: qualquer tipo além de ação marcado vira a marcação ligada.
            migrationBuilder.Sql("UPDATE trade_notes SET brokerage_fee_on_all_kinds = brokerage_fee_kinds <> ARRAY['Stock']::text[] AND cardinality(brokerage_fee_kinds) > 0;");

            migrationBuilder.DropColumn(
                name: "brokerage_fee_kinds",
                table: "trade_notes");
        }
    }
}
