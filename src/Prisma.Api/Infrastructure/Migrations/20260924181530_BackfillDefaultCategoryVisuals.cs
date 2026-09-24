using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillDefaultCategoryVisuals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Categorias padrão criadas antes da identidade visual (etapa 1.13) ganham ícone e cor.
            // Só onde o ícone está vazio: categoria criada ou editada pelo usuário não é tocada.
            // Valores copiados de DefaultCategories; migration é histórica e não referencia o domínio.
            migrationBuilder.Sql("UPDATE categories SET icon = 'house', color = COALESCE(color, '#8B5CF6') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Expense' AND name = 'Moradia';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'key-round', color = COALESCE(c.color, '#8B5CF6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Aluguel' AND p.name = 'Moradia';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'building-2', color = COALESCE(c.color, '#8B5CF6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Condomínio' AND p.name = 'Moradia';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'zap', color = COALESCE(c.color, '#8B5CF6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Energia' AND p.name = 'Moradia';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'droplets', color = COALESCE(c.color, '#8B5CF6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Água' AND p.name = 'Moradia';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'wifi', color = COALESCE(c.color, '#8B5CF6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Internet' AND p.name = 'Moradia';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'flame', color = COALESCE(c.color, '#8B5CF6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Gás' AND p.name = 'Moradia';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'utensils', color = COALESCE(color, '#F59E0B') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Expense' AND name = 'Alimentação';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'shopping-cart', color = COALESCE(c.color, '#F59E0B') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Mercado' AND p.name = 'Alimentação';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'utensils-crossed', color = COALESCE(c.color, '#F59E0B') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Restaurante' AND p.name = 'Alimentação';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'bike', color = COALESCE(c.color, '#F59E0B') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Delivery' AND p.name = 'Alimentação';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'croissant', color = COALESCE(c.color, '#F59E0B') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Padaria' AND p.name = 'Alimentação';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'car', color = COALESCE(color, '#3B82F6') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Expense' AND name = 'Transporte';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'fuel', color = COALESCE(c.color, '#3B82F6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Combustível' AND p.name = 'Transporte';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'car-taxi-front', color = COALESCE(c.color, '#3B82F6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'App de transporte' AND p.name = 'Transporte';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'square-parking', color = COALESCE(c.color, '#3B82F6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Estacionamento' AND p.name = 'Transporte';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'wrench', color = COALESCE(c.color, '#3B82F6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Manutenção' AND p.name = 'Transporte';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'bus', color = COALESCE(c.color, '#3B82F6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Transporte público' AND p.name = 'Transporte';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'heart-pulse', color = COALESCE(color, '#F43F5E') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Expense' AND name = 'Saúde';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'shield-plus', color = COALESCE(c.color, '#F43F5E') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Plano de saúde' AND p.name = 'Saúde';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'pill', color = COALESCE(c.color, '#F43F5E') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Farmácia' AND p.name = 'Saúde';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'stethoscope', color = COALESCE(c.color, '#F43F5E') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Consultas' AND p.name = 'Saúde';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'dumbbell', color = COALESCE(c.color, '#F43F5E') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Academia' AND p.name = 'Saúde';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'graduation-cap', color = COALESCE(color, '#6366F1') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Expense' AND name = 'Educação';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'presentation', color = COALESCE(c.color, '#6366F1') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Cursos' AND p.name = 'Educação';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'book-open', color = COALESCE(c.color, '#6366F1') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Livros' AND p.name = 'Educação';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'school', color = COALESCE(c.color, '#6366F1') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Mensalidade' AND p.name = 'Educação';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'party-popper', color = COALESCE(color, '#EC4899') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Expense' AND name = 'Lazer';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'tv', color = COALESCE(c.color, '#EC4899') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Streaming' AND p.name = 'Lazer';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'plane', color = COALESCE(c.color, '#EC4899') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Viagem' AND p.name = 'Lazer';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'beer', color = COALESCE(c.color, '#EC4899') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Bares' AND p.name = 'Lazer';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'clapperboard', color = COALESCE(c.color, '#EC4899') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Cinema' AND p.name = 'Lazer';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'shopping-bag', color = COALESCE(color, '#14B8A6') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Expense' AND name = 'Compras';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'shirt', color = COALESCE(c.color, '#14B8A6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Roupas' AND p.name = 'Compras';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'smartphone', color = COALESCE(c.color, '#14B8A6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Eletrônicos' AND p.name = 'Compras';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'sofa', color = COALESCE(c.color, '#14B8A6') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Casa' AND p.name = 'Compras';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'layers', color = COALESCE(color, '#06B6D4') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Expense' AND name = 'Serviços';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'repeat', color = COALESCE(c.color, '#06B6D4') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Assinaturas' AND p.name = 'Serviços';");
            migrationBuilder.Sql("UPDATE categories AS c SET icon = 'phone', color = COALESCE(c.color, '#06B6D4') FROM categories AS p WHERE c.parent_category_id = p.id AND c.icon IS NULL AND c.type = 'Expense' AND c.name = 'Telefonia' AND p.name = 'Serviços';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'landmark', color = COALESCE(color, '#64748B') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Expense' AND name = 'Impostos e Tarifas';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'shapes', color = COALESCE(color, '#94A3B8') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Expense' AND name = 'Outros';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'briefcase', color = COALESCE(color, '#84CC16') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Income' AND name = 'Salário';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'laptop', color = COALESCE(color, '#22D3EE') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Income' AND name = 'Freelance';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'sprout', color = COALESCE(color, '#A78BFA') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Income' AND name = 'Rendimentos';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'undo-2', color = COALESCE(color, '#FBBF24') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Income' AND name = 'Reembolso';");
            migrationBuilder.Sql("UPDATE categories SET icon = 'shapes', color = COALESCE(color, '#94A3B8') WHERE icon IS NULL AND parent_category_id IS NULL AND type = 'Income' AND name = 'Outros';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem volta: não há como distinguir o ícone preenchido aqui de um escolhido depois.
        }
    }
}
