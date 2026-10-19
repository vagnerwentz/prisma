using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketSyncRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_sync_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    window_from = table.Column<DateOnly>(type: "date", nullable: false),
                    window_to = table.Column<DateOnly>(type: "date", nullable: false),
                    fetched = table.Column<int>(type: "integer", nullable: false),
                    added = table.Column<int>(type: "integer", nullable: false),
                    revised = table.Column<int>(type: "integer", nullable: false),
                    unchanged = table.Column<int>(type: "integer", nullable: false),
                    source_error_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error_summary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_sync_runs", x => x.id);
                    table.CheckConstraint("ck_market_sync_runs_counts", "fetched >= 0 AND added >= 0 AND revised >= 0 AND unchanged >= 0");
                    table.CheckConstraint("ck_market_sync_runs_kind", "kind IN ('Scheduled', 'Startup', 'Manual')");
                    table.CheckConstraint("ck_market_sync_runs_outcome", "outcome IN ('Succeeded', 'SourceUnavailable', 'Failed', 'TimedOut')");
                    table.CheckConstraint("ck_market_sync_runs_series", "series IN ('Cdi')");
                    table.CheckConstraint("ck_market_sync_runs_window", "window_from <= window_to");
                });

            migrationBuilder.CreateIndex(
                name: "ix_market_sync_runs_series_started_at",
                table: "market_sync_runs",
                columns: new[] { "series", "started_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_sync_runs");
        }
    }
}
