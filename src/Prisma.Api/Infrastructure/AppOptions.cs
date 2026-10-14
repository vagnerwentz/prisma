namespace Prisma.Api.Infrastructure;

// O endereço público do app (seção "App"), para os links dos e-mails (docs/email.md, seção 2). A raiz do site, sem
// caminho: os links são montados a partir dela (confirmar-email?…).
public sealed class AppOptions
{
    public const string Section = "App";

    public Uri PublicUrl { get; init; } = new("https://prismamoney.com.br/");

    public bool IsValid() =>
        PublicUrl.IsAbsoluteUri && PublicUrl.Scheme is "https" or "http" && PublicUrl.AbsolutePath == "/"
        && string.IsNullOrEmpty(PublicUrl.Query);
}
