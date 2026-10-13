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
    // ConvertedToSymbol e ConvertedOn (I.7c1): o ativo virou outro ("BCFF11 · virou BTHF11 em 16/12/2024"). A nota
    // antiga se lança com o código antigo, como está no papel; a conversão é do Prisma.
    public sealed record Item(
        Guid Id, string Symbol, string Name, AssetKind Kind, bool IsActive, bool HasLogo, string? ConvertedToSymbol = null,
        DateOnly? ConvertedOn = null);

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
            var conversions = await db.CorporateEvents.AsNoTracking()
                .Where(e => (e.Kind == CorporateEventKind.Conversion || e.Kind == CorporateEventKind.Liquidation) && ids.Contains(e.AssetId))
                .Join(db.Assets.AsNoTracking(), e => e.TargetAssetId, a => a.Id, (e, target) => new { e.AssetId, target.Symbol, e.Date })
                .ToListAsync(ct);
            var convertedTo = conversions.GroupBy(c => c.AssetId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Date).First());
            return rows.Select(a =>
                {
                    var conversion = convertedTo.GetValueOrDefault(a.Id);
                    return new Item(a.Id, a.Symbol, a.DisplayName, a.Kind, a.IsActive, withLogo.Contains(a.Id), conversion?.Symbol, conversion?.Date);
                })
                .ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/assets", async (string? q, Handler handler, CancellationToken ct) => Results.Ok(await handler.Execute(q, ct)))
            .Produces<IReadOnlyList<Item>>(200);
}
