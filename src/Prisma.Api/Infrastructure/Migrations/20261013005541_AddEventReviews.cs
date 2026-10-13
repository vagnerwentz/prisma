using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    corporate_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reported_quantity = table.Column<long>(type: "bigint", nullable: true),
                    event_version = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_reviews", x => x.id);
                    table.CheckConstraint("ck_event_reviews_reported_quantity", "reported_quantity IS NULL OR (verdict = 'Disputed' AND reported_quantity BETWEEN 0 AND 100000000)");
                    table.CheckConstraint("ck_event_reviews_verdict", "verdict IN ('Confirmed', 'Disputed')");
                    table.ForeignKey(
                        name: "fk_event_reviews_corporate_events_corporate_event_id",
                        column: x => x.corporate_event_id,
                        principalTable: "corporate_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_event_reviews_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_reviews_corporate_event_id",
                table: "event_reviews",
                column: "corporate_event_id");

            migrationBuilder.CreateIndex(
                name: "ux_event_reviews_user_event",
                table: "event_reviews",
                columns: new[] { "user_id", "corporate_event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_reviews");
        }
    }
}
