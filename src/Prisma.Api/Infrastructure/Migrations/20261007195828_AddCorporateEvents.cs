using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCorporateEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corporate_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_date = table.Column<DateOnly>(type: "date", nullable: false),
                    from_quantity = table.Column<int>(type: "integer", nullable: false),
                    to_quantity = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corporate_events", x => x.id);
                    table.CheckConstraint("ck_corporate_events_kind", "kind IN ('Split', 'Conversion')");
                    table.CheckConstraint("ck_corporate_events_proportion", "from_quantity > 0 AND to_quantity > 0");
                    table.CheckConstraint("ck_corporate_events_target", "(kind = 'Conversion' AND target_asset_id IS NOT NULL AND target_asset_id <> asset_id) OR (kind <> 'Conversion' AND target_asset_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_corporate_events_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_corporate_events_assets_target_asset_id",
                        column: x => x.target_asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_corporate_events_asset_id",
                table: "corporate_events",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_corporate_events_target_asset_id",
                table: "corporate_events",
                column: "target_asset_id");

            migrationBuilder.CreateIndex(
                name: "ux_corporate_events_key",
                table: "corporate_events",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corporate_events");
        }
    }
}
