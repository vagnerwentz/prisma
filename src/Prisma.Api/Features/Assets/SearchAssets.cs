using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain.Market;

namespace Prisma.Api.Features.Assets;

// Busca no catálogo de ativos, para escolher um ativo ao cadastrar um rendimento (docs/investimentos.md,
// seção 8, etapa 3). Por pedaços do código ou do nome, sem diferenciar maiúsculas nem acentos (AssetSearch).
//
// Ordem: o código exato primeiro ("bbas3" → BBAS3), depois os códigos que começam pelo termo ("itsa" →
// ITSA3, ITSA4), depois o resto; dentro de cada grupo, os negociados antes dos que saíram da bolsa, e o
// código mais curto e em ordem alfabética. O ativo inativo aparece, marcado: pode haver rendimento antigo dele.
public static class SearchAssets
{
    public const int MaxResults = 20;

    // HasLogo: a tela pede o logo (GET /assets/{id}/logo) só de quem tem; os outros mostram o código.
    public sealed record Item(Guid Id, string Symbol, string Name, AssetKind Kind, bool IsActive, bool HasLogo);

    public sealed class Handler(AppDbContext db)
    {
        public async Task<IReadOnlyList<Item>> Execute(string? q, CancellationToken ct)
        {
            var term = AssetSearch.Term(q);
            if (term.Length == 0)
                return [];

            // O código é guardado em maiúsculas e sem espaço.
            var symbol = term.Replace(" ", "").ToUpperInvariant();

            // Cada palavra precisa aparecer, em qualquer ordem: "banco brasil" acha "Banco do Brasil".
            var query = db.Assets.AsNoTracking();
            foreach (var word in term.Split(' '))
                query = query.Where(a => a.SearchText.Contains(word));

            var rows = await query
                .OrderBy(a => a.Symbol == symbol ? 0 : a.Symbol.StartsWith(symbol) ? 1 : 2)
                .ThenBy(a => a.InactiveSince != null)
                .ThenBy(a => a.Symbol.Length)
                .ThenBy(a => a.Symbol)
                .Take(MaxResults)
                .ToListAsync(ct);

            var ids = rows.Select(a => a.Id).ToList();
            var withLogo = (await db.AssetLogos.AsNoTracking().Where(l => ids.Contains(l.AssetId)).Select(l => l.AssetId).ToListAsync(ct))
                .ToHashSet();
            return rows.Select(a => new Item(a.Id, a.Symbol, a.DisplayName, a.Kind, a.IsActive, withLogo.Contains(a.Id))).ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/assets", async (string? q, Handler handler, CancellationToken ct) => Results.Ok(await handler.Execute(q, ct)))
            .Produces<IReadOnlyList<Item>>(200);
}
