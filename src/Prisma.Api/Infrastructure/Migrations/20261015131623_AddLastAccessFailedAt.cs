using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLastAccessFailedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_access_failed_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            // Senhas erradas de antes não têm data: não dá para saber se caem na janela de 15 minutos. A contagem recomeça.
            migrationBuilder.Sql("UPDATE users SET access_failed_count = 0 WHERE access_failed_count > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_access_failed_at",
                table: "users");
        }
    }
}
