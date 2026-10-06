using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;

namespace Prisma.Api.Features.Assets;

// O logo guardado de um ativo (docs/investimentos.md, etapa 4). O navegador só fala com o Prisma: o fornecedor não
// fica sabendo o que a pessoa olha, e o logo aparece mesmo com ele fora do ar.
//
// Mostrado como <img>, um SVG não executa nada; aberto direto na aba, executaria. Por isso a CSP fecha tudo (nada de
// script, nada de buscar fora) e o nosniff impede o navegador de tratá-lo como outra coisa.
public static class GetAssetLogo
{
    public const string ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/assets/{id:guid}/logo", async (Guid id, HttpContext http, AppDbContext db, CancellationToken ct) =>
            {
                var logo = await db.AssetLogos.AsNoTracking()
                    .Where(l => l.AssetId == id)
                    .Select(l => new { l.Svg, l.Sha256 })
                    .SingleOrDefaultAsync(ct);
                if (logo is null)
                    return Results.NotFound();

                var etag = $"\"{Convert.ToHexStringLower(logo.Sha256)}\"";
                var headers = http.Response.Headers;
                headers.ETag = etag;
                // Privado: a rota exige sessão. Um dia basta: o logo muda raramente, e o ETag evita baixar de novo.
                headers.CacheControl = "private, max-age=86400";
                headers.XContentTypeOptions = "nosniff";
                headers.ContentSecurityPolicy = ContentSecurityPolicy;

                if (http.Request.Headers.IfNoneMatch == etag)
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                return Results.Text(logo.Svg, "image/svg+xml; charset=utf-8");
            })
            .Produces(200, contentType: "image/svg+xml")
            .Produces(304)
            .Produces(404);
}
