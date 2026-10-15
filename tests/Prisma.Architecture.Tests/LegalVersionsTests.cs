using Prisma.Api.Infrastructure.Privacy;
using Shouldly;

namespace Prisma.Architecture.Tests;

// O texto dos termos e da política vive no front (src/legal/*.md), e a versão vigente na API (LegalTerms), que decide
// quem precisa aceitar de novo (etapa A.4). São duas fontes da verdade: subir a versão de um lado só publicaria um texto
// novo sem pedir o aceite, ou pediria o aceite de um texto que não mudou. Este teste as liga.
public sealed class LegalVersionsTests
{
    [Theory]
    [InlineData("src/prisma-web/src/legal/terms-of-use.md", LegalTerms.TermsVersion)]
    [InlineData("src/prisma-web/src/legal/privacy-policy.md", LegalTerms.PrivacyVersion)]
    public void The_published_text_states_the_version_the_api_asks_to_accept(string file, string version)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), file));

        text.ShouldContain($"**Versão**: v{version} |");
    }

    // Sobe das pastas de saída dos testes até a raiz do repositório (onde está o CLAUDE.md).
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
