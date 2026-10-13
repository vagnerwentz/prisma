using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LiquidationEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_corporate_events_kind",
                table: "corporate_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_corporate_events_target",
                table: "corporate_events");

            migrationBuilder.AddColumn<long>(
                name: "cash_per_unit",
                table: "corporate_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "target_unit_price",
                table: "corporate_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_corporate_events_kind",
                table: "corporate_events",
                sql: "kind IN ('Split', 'Conversion', 'Liquidation')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_corporate_events_liquidation",
                table: "corporate_events",
                sql: "(kind = 'Liquidation' AND target_unit_price > 0 AND (cash_per_unit IS NULL OR cash_per_unit >= 0)) OR (kind <> 'Liquidation' AND target_unit_price IS NULL AND cash_per_unit IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_corporate_events_target",
                table: "corporate_events",
                sql: "(kind IN ('Conversion', 'Liquidation') AND target_asset_id IS NOT NULL AND target_asset_id <> asset_id) OR (kind NOT IN ('Conversion', 'Liquidation') AND target_asset_id IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A versão anterior não conhece a liquidação: os eventos saem, e a curadoria daquela versão recria os dela.
            migrationBuilder.Sql("DELETE FROM corporate_events WHERE kind = 'Liquidation';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_corporate_events_kind",
                table: "corporate_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_corporate_events_liquidation",
                table: "corporate_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_corporate_events_target",
                table: "corporate_events");

            migrationBuilder.DropColumn(
                name: "cash_per_unit",
                table: "corporate_events");

            migrationBuilder.DropColumn(
                name: "target_unit_price",
                table: "corporate_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_corporate_events_kind",
                table: "corporate_events",
                sql: "kind IN ('Split', 'Conversion')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_corporate_events_target",
                table: "corporate_events",
                sql: "(kind = 'Conversion' AND target_asset_id IS NOT NULL AND target_asset_id <> asset_id) OR (kind <> 'Conversion' AND target_asset_id IS NULL)");
        }
    }
}
