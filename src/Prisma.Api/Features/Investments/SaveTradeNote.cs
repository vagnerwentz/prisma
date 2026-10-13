using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;

namespace Prisma.Api.Features.Investments;

// Lançar e editar a nota digitada (docs/investimentos.md, "I.7b1 API"). O domínio valida a nota; aqui se carrega o
// tipo de cada ativo do catálogo, confere a carteira inteira com a nota nova e põe os ativos na carteira.
public static class SaveTradeNote
{
    public sealed record Line(Guid AssetId, TradeSide Side, long Quantity, long UnitPriceCents);

    // BrokerageFeeKinds (I.7b3): os tipos em que a corretagem foi cobrada. Sem ele, vale a marcação de antes
    // (BrokerageFeeOnAllKinds), como manda uma aba aberta antes do deploy.
    public sealed record Request(
        string Broker, DateOnly Date, long FeesCents, long BrokerageFeeCents, bool? BrokerageFeeOnAllKinds, IReadOnlyList<Line> Trades,
        IReadOnlyList<AssetKind>? BrokerageFeeKinds = null);

    // Tetos que deixam qualquer soma longe do limite do long: até 100 milhões de unidades a R$ 1 milhão.
    private const long MaxQuantity = 100_000_000;
    private const long MaxUnitPriceCents = 100_000_000;
    private const long MaxCostCents = 10_000_000_000;
    private const int MaxTrades = 100;

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Broker).NotEmpty().WithMessage("Escolha a corretora.")
                .MaximumLength(TradeNote.BrokerMaxLength).WithMessage($"O nome da corretora pode ter até {TradeNote.BrokerMaxLength} caracteres.");
            RuleFor(x => x.FeesCents).InclusiveBetween(0, MaxCostCents).WithMessage("Taxas inválidas.");
            RuleFor(x => x.BrokerageFeeCents).InclusiveBetween(0, MaxCostCents).WithMessage("Corretagem inválida.");
            // Tipo desconhecido não passa da leitura do JSON (enum por nome, sem número): sobra limitar o tamanho.
            RuleFor(x => x.BrokerageFeeKinds).Must(k => k is null || k.Count <= 20).WithMessage("Tipos demais na corretagem.");
            RuleFor(x => x.Trades).NotEmpty().WithMessage("Inclua ao menos uma operação na nota.")
                .Must(t => t.Count <= MaxTrades).WithMessage($"Uma nota tem até {MaxTrades} operações.");
            RuleForEach(x => x.Trades).ChildRules(line =>
            {
                line.RuleFor(l => l.AssetId).NotEmpty().WithMessage("Escolha o ativo.");
                line.RuleFor(l => l.Side).IsInEnum().WithMessage("Escolha compra ou venda.");
                line.RuleFor(l => l.Quantity).GreaterThan(0).WithMessage("A quantidade precisa ser maior que zero.")
                    .LessThanOrEqualTo(MaxQuantity).WithMessage("Quantidade grande demais.");
                line.RuleFor(l => l.UnitPriceCents).GreaterThan(0).WithMessage("O preço precisa ser maior que zero.")
                    .LessThanOrEqualTo(MaxUnitPriceCents).WithMessage("Preço grande demais.");
            });
        }
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, ILogger<Handler> logger)
    {
        public Task<Result<TradeNoteResponse>> Create(Request req, CancellationToken ct) => Save(null, req, ct);

        public Task<Result<TradeNoteResponse>> Update(Guid id, Request req, CancellationToken ct) => Save(id, req, ct);

        // Pôr o ativo na carteira esbarra no índice único se outro pedido o pôs ao mesmo tempo: refaz uma vez (como
        // o provento).
        private async Task<Result<TradeNoteResponse>> Save(Guid? id, Request req, CancellationToken ct)
        {
            try
            {
                return await Attempt(id, req, ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation(HoldingConfiguration.AssetIndex))
            {
                db.ChangeTracker.Clear();
                return await Attempt(id, req, ct);
            }
        }

        private async Task<Result<TradeNoteResponse>> Attempt(Guid? id, Request req, CancellationToken ct)
        {
            var assets = await TradeBook.Assets(db, req.Trades.Select(t => t.AssetId), ct);
            if (req.Trades.Any(t => !assets.ContainsKey(t.AssetId)))
                return new Error(ErrorType.NotFound, "Ativo não encontrado.");

            // O tipo do ativo fica na linha: a corretagem segue o que a nota era quando foi lançada.
            var trades = req.Trades.Select(t => new Trade(t.AssetId, assets[t.AssetId].Kind, t.Side, t.Quantity, t.UnitPriceCents)).ToList();
            var brokerageKinds = TradeNote.BrokerageKindsFrom(req.BrokerageFeeKinds, req.BrokerageFeeOnAllKinds);

            // As outras notas só entram na conferência: lidas sem acompanhamento, salvar não as vasculha.
            var notes = await TradeBook.Notes(db, ct);
            TradeNote note;
            if (id is null)
            {
                var created = TradeNote.Create(
                    currentUser.UserId, req.Broker, req.Date, trades, req.FeesCents, req.BrokerageFeeCents, brokerageKinds);
                if (!created.IsSuccess)
                    return created.Error;
                note = created.Value;
                // Por último: no mesmo dia, depois das que já existem.
                notes.Add(note);
            }
            else
            {
                var existing = await db.TradeNotes.SingleOrDefaultAsync(n => n.Id == id, ct);
                if (existing is null)
                    return new Error(ErrorType.NotFound, "Nota não encontrada.");
                note = existing;
                if (note.Update(req.Broker, req.Date, trades, req.FeesCents, req.BrokerageFeeCents, brokerageKinds) is { } error)
                    return error;
                // A data pode ter mudado: no dia novo, a nota entra na ordem em que foi lançada, como na leitura.
                notes = [.. notes.Where(n => n.Id != id).Append(note).OrderBy(n => n.Date).ThenBy(n => n.CreatedAt).ThenBy(n => n.Id)];
            }

            var positions = await TradeBook.Positions(db, notes, ct);
            if (!positions.IsSuccess)
                return positions.Error;

            if (id is null)
                db.TradeNotes.Add(note);
            foreach (var assetId in trades.Select(t => t.AssetId).Distinct())
                await Portfolio.Ensure(db, currentUser.UserId, assetId, ct);
            await db.SaveChangesAsync(ct);

            if (id is null)
                logger.TradeNoteCreated(note.Id, note.Trades.Count);
            else
                logger.TradeNoteEdited(note.Id);
            return TradeNoteResponse.From(note, assets);
        }
    }

    public static void MapCreate(IEndpointRouteBuilder app) =>
        app.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Create(request, ct);
                return result.IsSuccess ? Results.Created((string?)null, result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<TradeNoteResponse>(201)
            .ProducesValidationProblem()
            .ProducesProblem(404);

    public static void MapUpdate(IEndpointRouteBuilder app) =>
        app.MapPut("/{id:guid}", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Update(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<TradeNoteResponse>(200)
            .ProducesValidationProblem()
            .ProducesProblem(404);
}
