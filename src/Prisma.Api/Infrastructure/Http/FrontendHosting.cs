using Microsoft.AspNetCore.Authentication;
using System.IO.Compression;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Auth;

namespace Prisma.Api.Infrastructure.Http;

// Em produção a API serve o React no mesmo domínio (CLAUDE.md, seção 7): o cookie de sessão vai
// junto sem CORS. Tudo sob /api é a API; o resto é o frontend.
public static class FrontendHosting
{
    public const string ApiPrefix = "/api";

    // Os arquivos de /assets têm o hash do conteúdo no nome: podem ficar em cache para sempre.
    // O index.html e o resto são revalidados, para uma versão nova chegar no próximo acesso.
    private const string Immutable = "public, max-age=31536000, immutable";
    private const string Revalidate = "no-cache";
    private const string LandingFile = "/landing.html";

    // Compressão do front (docs/landing.md, D20): HTML, CSS, JS e SVG, que não levam dado de ninguém. Só o ramo do front a
    // usa (UseFrontendAndApiPrefix): as respostas da API ficam sem compressão, porque dado do usuário comprimido junto com
    // texto que outro site consegue injetar abre a porta para o ataque BREACH. "Fastest": comprime a cada pedido, e o
    // ganho do nível máximo não paga a CPU.
    public static IServiceCollection AddFrontendCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Append("image/svg+xml");
        });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        return services;
    }

    public static void UseFrontendAndApiPrefix(this WebApplication app)
    {
        var files = app.Environment.WebRootFileProvider;

        app.MapWhen(context => !context.Request.Path.StartsWithSegments(ApiPrefix), frontend =>
        {
            frontend.UseResponseCompression();

            // A página de apresentação só existe em "/" (ShowsLanding): o arquivo dela não abre pelo nome.
            frontend.Use(async (context, next) =>
            {
                if (context.Request.Path.Equals(LandingFile, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                await next(context);
            });
            frontend.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = SetCacheControl });

            // Rota de tela (/lancamentos, /contas/…) devolve o index.html e o React Router decide.
            // Caminho com extensão que não existe (o pedaço de uma versão antiga) é 404: devolver o
            // index.html no lugar de um .js quebraria a tela com um erro confuso.
            frontend.Run(async context =>
            {
                var index = files.GetFileInfo("index.html");
                var isScreen = (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                    && !Path.HasExtension(context.Request.Path.Value);

                if (!isScreen || !index.Exists)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var page = await ShowsLanding(context, files) ? files.GetFileInfo(LandingFile) : index;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = Revalidate;
                await context.Response.SendFileAsync(page);
            });
        });

        // Daqui em diante só chegam pedidos a /api, e as rotas da API são mapeadas sem o prefixo.
        app.UsePathBase(ApiPrefix);
    }

    // Página de apresentação (docs/landing.md): só na raiz, para quem chega sem sessão. O botão dela leva ao cadastro,
    // então com o cadastro fechado ela não aparece e "/" segue para o login, como antes. Quem tem sessão vê o Resumo no
    // mesmo endereço. A sessão se confere aqui porque o frontend roda antes da autenticação do pipeline.
    private static async Task<bool> ShowsLanding(HttpContext context, IFileProvider files)
    {
        if (context.Request.Path != "/" || !files.GetFileInfo(LandingFile).Exists)
            return false;
        if (!context.RequestServices.GetRequiredService<IOptions<RegistrationOptions>>().Value.Open)
            return false;
        return !(await context.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Succeeded;
    }

    private static void SetCacheControl(StaticFileResponseContext context) =>
        context.Context.Response.Headers.CacheControl =
            context.Context.Request.Path.StartsWithSegments("/assets") ? Immutable : Revalidate;
}
