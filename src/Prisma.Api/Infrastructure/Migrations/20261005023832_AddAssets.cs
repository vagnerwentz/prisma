using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    long_name = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    search_text = table.Column<string>(type: "text", nullable: false),
                    inactive_since = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assets", x => x.id);
                    table.CheckConstraint("ck_assets_kind", "kind IN ('Stock', 'Unit', 'Fii', 'Etf', 'FiInfra', 'FiAgro', 'Fip', 'Fidc', 'OtherFund', 'Bdr', 'Unknown')");
                    table.CheckConstraint("ck_assets_symbol", "symbol ~ '^[A-Z0-9]+$'");
                });

            migrationBuilder.CreateIndex(
                name: "ux_assets_symbol",
                table: "assets",
                column: "symbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assets");
        }
    }
}
