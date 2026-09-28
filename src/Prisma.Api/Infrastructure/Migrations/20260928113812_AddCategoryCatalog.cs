using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prisma.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Quem já existe recebeu o catálogo original (versão 1); a sincronização entrega o resto
            // (docs/fase-2.md, 2.11). O cadastro sempre grava a versão atual.
            migrationBuilder.AddColumn<int>(
                name: "category_catalog_version",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "template_key",
                table: "categories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_categories_template_key",
                table: "categories",
                columns: new[] { "user_id", "template_key" },
                unique: true,
                filter: "template_key IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_categories_template_key",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "category_catalog_version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "template_key",
                table: "categories");
        }
    }
}
