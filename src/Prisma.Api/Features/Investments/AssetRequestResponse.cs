using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.Investments;
using Prisma.Domain.Market;

namespace Prisma.Api.Features.Investments;

// Esperando: o código não está no catálogo. Chegou: está (ativo ou inativo), e a pessoa ainda não tem nota com ele.
public enum AssetRequestStatus { Waiting, Arrived }

// O ativo que chegou, para abrir a nota com ele. Name é o nome mostrado (Asset.DisplayName).
public sealed record ArrivedAsset(Guid Id, string Name, AssetKind Kind, bool HasLogo, bool IsActive);

public sealed record AssetRequestResponse(Guid Id, string Symbol, DateOnly RequestedOn, AssetRequestStatus Status, ArrivedAsset? Asset);

// O estado de cada pedido é calculado na leitura, nunca gravado (docs/investimentos.md, I.7c3): o ativo pode chegar
// pela brapi ou pela curadoria, e nenhuma tarefa precisa avisar o pedido.
internal static class AssetRequestBook
{
    // Os pedidos com o estado. Resolvidos (o ativo chegou e a pessoa já tem nota com ele) ficam de fora da lista.
    public static async Task<IReadOnlyList<AssetRequestResponse>> Describe(
        AppDbContext db, IReadOnlyList<AssetRequest> requests, CancellationToken ct, bool keepResolved = false)
    {
        var symbols = requests.Select(r => r.Symbol).Distinct().ToList();
        var arrived = await db.Assets.AsNoTracking()
            .Where(a => symbols.Contains(a.Symbol))
            .Select(a => new { Asset = a, HasLogo = db.AssetLogos.Any(l => l.AssetId == a.Id) })
            .ToDictionaryAsync(x => x.Asset.Symbol, StringComparer.Ordinal, ct);

        var arrivedIds = arrived.Values.Select(x => x.Asset.Id).ToList();
        var traded = keepResolved || arrivedIds.Count == 0
            ? []
            : (await db.TradeNotes.AsNoTracking()
                .SelectMany(n => n.Trades.Select(t => t.AssetId))
                .Where(id => arrivedIds.Contains(id))
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var result = new List<AssetRequestResponse>(requests.Count);
        foreach (var request in requests)
        {
            var requestedOn = SaoPauloTime.DateOf(request.CreatedAt);
            if (!arrived.TryGetValue(request.Symbol, out var x))
            {
                result.Add(new(request.Id, request.Symbol, requestedOn, AssetRequestStatus.Waiting, null));
                continue;
            }

            if (traded.Contains(x.Asset.Id))
                continue;
            var asset = new ArrivedAsset(x.Asset.Id, x.Asset.DisplayName, x.Asset.Kind, x.HasLogo, x.Asset.IsActive);
            result.Add(new(request.Id, request.Symbol, requestedOn, AssetRequestStatus.Arrived, asset));
        }

        return result;
    }

    // Um pedido só (resposta de criar e de restaurar): sempre com o estado, mesmo resolvido.
    public static async Task<AssetRequestResponse> Describe(AppDbContext db, AssetRequest request, CancellationToken ct) =>
        (await Describe(db, [request], ct, keepResolved: true)).Single();

    // Pedidos que esperam: o código ainda não está no catálogo. Os que chegaram não contam no limite.
    public static Task<int> Waiting(AppDbContext db, CancellationToken ct) =>
        db.AssetRequests.CountAsync(r => !db.Assets.Any(a => a.Symbol == r.Symbol), ct);

    public static Error TooMany =>
        new(ErrorType.Conflict, $"Você tem {AssetRequest.MaxWaiting} avisos esperando. Cancele um ou espere algum chegar.");
}
