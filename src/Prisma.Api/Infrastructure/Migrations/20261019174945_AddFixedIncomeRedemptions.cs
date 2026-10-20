using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFixedIncomeRedemptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fixed_income_redemptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    deposit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    redemption_date = table.Column<DateOnly>(type: "date", nullable: false),
                    gross_cents = table.Column<long>(type: "bigint", nullable: false),
                    principal_cents = table.Column<long>(type: "bigint", nullable: false),
                    yield_cents = table.Column<long>(type: "bigint", nullable: false),
                    iof_cents = table.Column<long>(type: "bigint", nullable: true),
                    ir_cents = table.Column<long>(type: "bigint", nullable: true),
                    tax_cents = table.Column<long>(type: "bigint", nullable: false),
                    net_cents = table.Column<long>(type: "bigint", nullable: false),
                    tax_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tax_rule_version = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fixed_income_redemptions", x => x.id);
                    table.CheckConstraint("ck_fixed_income_redemptions_gross", "gross_cents >= 1 AND gross_cents <= 100000000000000");
                    table.CheckConstraint("ck_fixed_income_redemptions_split", "principal_cents >= 0 AND yield_cents >= 0 AND principal_cents + yield_cents = gross_cents");
                    table.CheckConstraint("ck_fixed_income_redemptions_tax", "tax_cents >= 0 AND tax_cents <= yield_cents AND net_cents = gross_cents - tax_cents");
                    table.CheckConstraint("ck_fixed_income_redemptions_tax_detail", "(tax_source = 'Estimated' AND iof_cents >= 0 AND ir_cents >= 0 AND iof_cents + ir_cents = tax_cents AND tax_rule_version IS NOT NULL) OR (tax_source = 'Informed' AND iof_cents IS NULL AND ir_cents IS NULL AND tax_rule_version IS NULL)");
                    table.CheckConstraint("ck_fixed_income_redemptions_tax_source", "tax_source IN ('Estimated', 'Informed')");
                    table.ForeignKey(
                        name: "fk_fixed_income_redemptions_fixed_income_deposits_deposit_id",
                        column: x => x.deposit_id,
                        principalTable: "fixed_income_deposits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fixed_income_redemptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fixed_income_redemptions_deposit_id_redemption_date",
                table: "fixed_income_redemptions",
                columns: new[] { "deposit_id", "redemption_date" });

            migrationBuilder.CreateIndex(
                name: "ux_fixed_income_redemptions_user_request",
                table: "fixed_income_redemptions",
                columns: new[] { "user_id", "client_request_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fixed_income_redemptions");
        }
    }
}
