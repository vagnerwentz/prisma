using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLastSeenAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_seen_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            // Quem já confirmou o e-mail pode ter usado o app antes da coluna: vale o instante desta migration ("visto no
            // máximo até aqui"), para que nenhuma conta ativa pareça sumida antes de o primeiro pedido carimbar de
            // verdade. Quem nunca confirmou o e-mail não pôde entrar, e fica vazio ("nunca usou").
            migrationBuilder.Sql("UPDATE users SET last_seen_at = now() WHERE email_confirmed;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_seen_at",
                table: "users");
        }
    }
}
