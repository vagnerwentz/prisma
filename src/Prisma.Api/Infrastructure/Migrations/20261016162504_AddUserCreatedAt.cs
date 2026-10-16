using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            // Contas de antes da coluna não têm a data de nascimento. Vale o registro mais antigo que a pessoa ainda tem
            // (o aceite dos termos, o e-mail de confirmação, categorias, contas, lançamentos...): nunca é antes do
            // cadastro de verdade, então a data é "no máximo esta". Quem não tem nada recebe o instante da migration.
            // O que foi apagado de vez (excluídos há mais de 30 min) não conta: não existe mais para olhar.
            migrationBuilder.Sql("""
                UPDATE users u SET created_at = COALESCE(
                    LEAST(
                        u.terms_accepted_at,
                        (SELECT min(created_at) FROM outbox_emails       WHERE user_id = u.id),
                        (SELECT min(created_at) FROM categories          WHERE user_id = u.id),
                        (SELECT min(created_at) FROM accounts            WHERE user_id = u.id),
                        (SELECT min(created_at) FROM statements          WHERE user_id = u.id),
                        (SELECT min(created_at) FROM transactions        WHERE user_id = u.id),
                        (SELECT min(created_at) FROM installment_purchases WHERE user_id = u.id),
                        (SELECT min(created_at) FROM recurrences         WHERE user_id = u.id),
                        (SELECT min(created_at) FROM holdings            WHERE user_id = u.id),
                        (SELECT min(created_at) FROM trade_notes         WHERE user_id = u.id),
                        (SELECT min(created_at) FROM asset_requests      WHERE user_id = u.id),
                        (SELECT min(created_at) FROM event_reviews       WHERE user_id = u.id)),
                    now());
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "users");
        }
    }
}
