using Prisma.Api.Infrastructure.Auth;

namespace Prisma.Api.Infrastructure.Privacy;

// As versões vigentes dos termos de uso e da política de privacidade (etapa A.4), e o aceite de cada pessoa. O texto
// fica no front (src/prisma-web/src/legal/*.md, publicado em /termos e /privacidade); o teste LegalTermsTests confere
// que a versão escrita lá é a daqui.
//
// Mudou a versão de um dos dois, quem aceitou a anterior vê a tela de aceite ao entrar (SessionUser.MustAcceptTerms):
// os termos prometem pedir o novo aceite no app, e continuar usando não substitui o aceite. Só suba a versão em
// mudança que pede aceite; correção de redação muda o texto e fica na mesma versão.
public static class LegalTerms
{
    public const string TermsVersion = "1.0";
    public const string PrivacyVersion = "1.0";

    public static bool MustAccept(AppUser user) =>
        user.TermsVersion != TermsVersion || user.PrivacyVersion != PrivacyVersion;

    // O aceite: as versões vigentes e o instante. A caixa aceita os termos, declara ter lido a política (que informa, não
    // pede consentimento) e ter 18 anos ou mais.
    public static void Accept(AppUser user, DateTime utcNow)
    {
        user.TermsVersion = TermsVersion;
        user.PrivacyVersion = PrivacyVersion;
        user.TermsAcceptedAt = utcNow;
    }
}
