using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmExistingEmails : Migration
    {
        /// <inheritdoc />
        // A confirmação de e-mail passa a ser exigida no login (docs/email.md, 5.1). Quem se cadastrou antes nunca recebeu
        // o link: fica confirmado, para ninguém ficar do lado de fora depois do deploy.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE users SET email_confirmed = TRUE WHERE NOT email_confirmed;");
        }

        /// <inheritdoc />
        // Sem volta: não há como saber quem estava sem confirmar antes.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
