using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedCategoriesForUsersWithoutAny : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Usuários criados antes da etapa 1.6 não receberam o conjunto padrão de categorias.
            // Recebem agora, já com ícone e cor. Quem tem qualquer categoria (mesmo excluída) não
            // é tocado. Valores copiados de DefaultCategories; migration não referencia o domínio.
            migrationBuilder.Sql("""
WITH targets AS (
    SELECT u.id AS user_id FROM users u
    WHERE NOT EXISTS (SELECT 1 FROM categories c WHERE c.user_id = u.id)
),
root_seed(type, name, icon, color) AS (VALUES ('Expense', 'Moradia', 'house', '#8B5CF6'), ('Expense', 'Alimentação', 'utensils', '#F59E0B'), ('Expense', 'Transporte', 'car', '#3B82F6'), ('Expense', 'Saúde', 'heart-pulse', '#F43F5E'), ('Expense', 'Educação', 'graduation-cap', '#6366F1'), ('Expense', 'Lazer', 'party-popper', '#EC4899'), ('Expense', 'Compras', 'shopping-bag', '#14B8A6'), ('Expense', 'Serviços', 'layers', '#06B6D4'), ('Expense', 'Impostos e Tarifas', 'landmark', '#64748B'), ('Expense', 'Outros', 'shapes', '#94A3B8'), ('Income', 'Salário', 'briefcase', '#84CC16'), ('Income', 'Freelance', 'laptop', '#22D3EE'), ('Income', 'Rendimentos', 'sprout', '#A78BFA'), ('Income', 'Reembolso', 'undo-2', '#FBBF24'), ('Income', 'Outros', 'shapes', '#94A3B8')),
sub_seed(type, parent, name, icon) AS (VALUES ('Expense', 'Moradia', 'Aluguel', 'key-round'), ('Expense', 'Moradia', 'Condomínio', 'building-2'), ('Expense', 'Moradia', 'Energia', 'zap'), ('Expense', 'Moradia', 'Água', 'droplets'), ('Expense', 'Moradia', 'Internet', 'wifi'), ('Expense', 'Moradia', 'Gás', 'flame'), ('Expense', 'Alimentação', 'Mercado', 'shopping-cart'), ('Expense', 'Alimentação', 'Restaurante', 'utensils-crossed'), ('Expense', 'Alimentação', 'Delivery', 'bike'), ('Expense', 'Alimentação', 'Padaria', 'croissant'), ('Expense', 'Transporte', 'Combustível', 'fuel'), ('Expense', 'Transporte', 'App de transporte', 'car-taxi-front'), ('Expense', 'Transporte', 'Estacionamento', 'square-parking'), ('Expense', 'Transporte', 'Manutenção', 'wrench'), ('Expense', 'Transporte', 'Transporte público', 'bus'), ('Expense', 'Saúde', 'Plano de saúde', 'shield-plus'), ('Expense', 'Saúde', 'Farmácia', 'pill'), ('Expense', 'Saúde', 'Consultas', 'stethoscope'), ('Expense', 'Saúde', 'Academia', 'dumbbell'), ('Expense', 'Educação', 'Cursos', 'presentation'), ('Expense', 'Educação', 'Livros', 'book-open'), ('Expense', 'Educação', 'Mensalidade', 'school'), ('Expense', 'Lazer', 'Streaming', 'tv'), ('Expense', 'Lazer', 'Viagem', 'plane'), ('Expense', 'Lazer', 'Bares', 'beer'), ('Expense', 'Lazer', 'Cinema', 'clapperboard'), ('Expense', 'Compras', 'Roupas', 'shirt'), ('Expense', 'Compras', 'Eletrônicos', 'smartphone'), ('Expense', 'Compras', 'Casa', 'sofa'), ('Expense', 'Serviços', 'Assinaturas', 'repeat'), ('Expense', 'Serviços', 'Telefonia', 'phone')),
inserted_roots AS (
    INSERT INTO categories (id, user_id, name, type, parent_category_id, icon, color, created_at, updated_at)
    SELECT gen_random_uuid(), t.user_id, r.name, r.type, NULL, r.icon, r.color, now(), now()
    FROM targets t CROSS JOIN root_seed r
    RETURNING id, user_id, name, type, color
)
INSERT INTO categories (id, user_id, name, type, parent_category_id, icon, color, created_at, updated_at)
SELECT gen_random_uuid(), r.user_id, s.name, s.type, r.id, s.icon, r.color, now(), now()
FROM sub_seed s JOIN inserted_roots r ON r.type = s.type AND r.name = s.parent;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem volta: as categorias podem já ter transações vinculadas.
        }
    }
}
