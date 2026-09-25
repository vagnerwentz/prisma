using Microsoft.AspNetCore.StaticFiles;

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

    public static void UseFrontendAndApiPrefix(this WebApplication app)
    {
        var files = app.Environment.WebRootFileProvider;

        app.MapWhen(context => !context.Request.Path.StartsWithSegments(ApiPrefix), frontend =>
        {
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

                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = Revalidate;
                await context.Response.SendFileAsync(index);
            });
        });

        // Daqui em diante só chegam pedidos a /api, e as rotas da API são mapeadas sem o prefixo.
        app.UsePathBase(ApiPrefix);
    }

    private static void SetCacheControl(StaticFileResponseContext context) =>
        context.Context.Response.Headers.CacheControl =
            context.Context.Request.Path.StartsWithSegments("/assets") ? Immutable : Revalidate;
}
