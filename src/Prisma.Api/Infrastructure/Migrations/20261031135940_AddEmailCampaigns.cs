using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false),
                    recipients = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_campaigns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_campaign_recipients",
                columns: table => new
                {
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_campaign_recipients", x => new { x.campaign_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_email_campaign_recipients_email_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "email_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_email_campaign_recipients_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_campaign_recipients_user_id_kind_created_at",
                table: "email_campaign_recipients",
                columns: new[] { "user_id", "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_email_campaigns_client_request_id",
                table: "email_campaigns",
                column: "client_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_campaigns_created_at",
                table: "email_campaigns",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_campaign_recipients");

            migrationBuilder.DropTable(
                name: "email_campaigns");
        }
    }
}
