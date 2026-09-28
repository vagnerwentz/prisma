using System.Text.RegularExpressions;
using Prisma.Domain.Categories;
using Shouldly;

namespace Prisma.Architecture.Tests;

// O backend grava o nome do ícone de cada categoria padrão, e o front o desenha por um mapa fechado
// (categoryIcons.ts), para não levar o Lucide inteiro no pacote. São duas fontes da verdade: um ícone
// novo no catálogo sem entrada no mapa aparecia como o círculo tracejado, que lê como "carregando"
// (docs/fase-2.md, 2.11, regra 7). Este teste as liga.
public sealed partial class CategoryIconsTests
{
    private const string IconMapPath = "src/prisma-web/src/components/brand/categoryIcons.ts";

    [Fact]
    public void Every_catalog_icon_is_in_the_front_icon_map()
    {
        var mapped = IconKeys().Matches(File.ReadAllText(Path.Combine(RepositoryRoot(), IconMapPath)))
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        mapped.ShouldNotBeEmpty();
        DefaultCategories.Icons.Where(icon => !mapped.Contains(icon)).ShouldBeEmpty();
    }

    // Sobe das pastas de saída dos testes até a raiz do repositório (onde está o CLAUDE.md).
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }

    // As chaves do mapa: 'house': House, 'key-round': KeyRound, ...
    [GeneratedRegex(@"'([a-z0-9-]+)'\s*:\s*[A-Z]\w*")]
    private static partial Regex IconKeys();
}
