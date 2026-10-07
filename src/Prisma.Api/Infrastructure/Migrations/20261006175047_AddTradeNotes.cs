using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trade_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    broker = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    trade_date = table.Column<DateOnly>(type: "date", nullable: false),
                    fees_cents = table.Column<long>(type: "bigint", nullable: false),
                    brokerage_fee_cents = table.Column<long>(type: "bigint", nullable: false),
                    brokerage_fee_on_all_kinds = table.Column<bool>(type: "boolean", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_notes", x => x.id);
                    table.CheckConstraint("ck_trade_notes_broker", "length(btrim(broker)) > 0");
                    table.CheckConstraint("ck_trade_notes_costs", "fees_cents >= 0 AND brokerage_fee_cents >= 0");
                    table.ForeignKey(
                        name: "fk_trade_notes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trades",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<long>(type: "bigint", nullable: false),
                    unit_price_cents = table.Column<long>(type: "bigint", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    trade_note_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trades", x => x.id);
                    table.CheckConstraint("ck_trades_asset_kind", "asset_kind IN ('Stock', 'Unit', 'Fii', 'Etf', 'FiInfra', 'FiAgro', 'Fip', 'Fidc', 'OtherFund', 'Bdr', 'Unknown')");
                    table.CheckConstraint("ck_trades_quantity", "quantity > 0");
                    table.CheckConstraint("ck_trades_side", "side IN ('Buy', 'Sell')");
                    table.CheckConstraint("ck_trades_unit_price", "unit_price_cents > 0");
                    table.ForeignKey(
                        name: "fk_trades_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trades_trade_notes_trade_note_id",
                        column: x => x.trade_note_id,
                        principalTable: "trade_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trade_notes_user_id_trade_date",
                table: "trade_notes",
                columns: new[] { "user_id", "trade_date" });

            migrationBuilder.CreateIndex(
                name: "ix_trades_asset_id",
                table: "trades",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_trades_trade_note_id_line_number",
                table: "trades",
                columns: new[] { "trade_note_id", "line_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trades");

            migrationBuilder.DropTable(
                name: "trade_notes");
        }
    }
}
