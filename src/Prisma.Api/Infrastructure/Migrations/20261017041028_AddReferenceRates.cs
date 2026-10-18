using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReferenceRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reference_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reference_date = table.Column<DateOnly>(type: "date", nullable: false),
                    daily_rate_e8 = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    fetched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revised_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reference_rates", x => x.id);
                    table.CheckConstraint("ck_reference_rates_rate", "daily_rate_e8 >= 1 AND daily_rate_e8 < 1000000");
                    table.CheckConstraint("ck_reference_rates_series", "series IN ('Cdi')");
                });

            migrationBuilder.CreateIndex(
                name: "ux_reference_rates_series_date",
                table: "reference_rates",
                columns: new[] { "series", "reference_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reference_rates");
        }
    }
}
