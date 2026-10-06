using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Categories;
using Prisma.Domain.Investments;
using Prisma.Domain.Market;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Investments;

// Lançar e editar provento (docs/investimentos.md, etapa 5b). O domínio decide (Transaction.CreatePayout e
// UpdatePayout); aqui se carrega a conta, o ativo e a categoria "Rendimentos", e o ativo entra na carteira junto.
public static class SavePayout
{
    public sealed record Request(Guid AccountId, Guid AssetId, PayoutKind Kind, long AmountCents, DateOnly Date);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.AccountId).NotEmpty().WithMessage("Escolha a conta onde o dinheiro caiu.");
            RuleFor(x => x.AssetId).NotEmpty().WithMessage("Escolha o ativo.");
            RuleFor(x => x.Kind).IsInEnum().WithMessage("Tipo de provento inválido.");
            RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("O valor deve ser maior que zero.");
        }
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, ILogger<Handler> logger)
    {
        public Task<Result<PayoutResponse>> Create(Request req, CancellationToken ct) => Save(null, req, ct);

        public Task<Result<PayoutResponse>> Update(Guid id, Request req, CancellationToken ct) => Save(id, req, ct);

        // Pôr o ativo na carteira esbarra no índice único se outro pedido o pôs ao mesmo tempo: refaz uma vez, e
        // na segunda o ativo já está lá.
        private async Task<Result<PayoutResponse>> Save(Guid? id, Request req, CancellationToken ct)
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

        private async Task<Result<PayoutResponse>> Attempt(Guid? id, Request req, CancellationToken ct)
        {
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == req.AccountId, ct);
            if (account is null)
                return new Error(ErrorType.NotFound, "Conta não encontrada.");

            var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == req.AssetId, ct);
            if (asset is null)
                return new Error(ErrorType.NotFound, "Ativo não encontrado.");

            var category = await db.Categories.FirstOrDefaultAsync(c => c.TemplateKey == DefaultCategories.InvestmentIncomeKey, ct);

            Transaction payout;
            if (id is null)
            {
                var created = Transaction.CreatePayout(currentUser.UserId, account, asset.Id, req.Kind, req.AmountCents, req.Date, category);
                if (!created.IsSuccess)
                    return created.Error;
                payout = created.Value;
                db.Transactions.Add(payout);
            }
            else
            {
                var existing = await db.Transactions.SingleOrDefaultAsync(t => t.Id == id, ct);
                if (existing is null)
                    return new Error(ErrorType.NotFound, "Provento não encontrado.");
                payout = existing;

                // Mantém a categoria que a pessoa tiver escolhido depois; só a preenche se estiver vazia.
                var updated = payout.UpdatePayout(account, asset.Id, req.Kind, req.AmountCents, req.Date,
                    payout.CategoryId is { } current ? await db.Categories.FirstOrDefaultAsync(c => c.Id == current, ct) : category);
                if (!updated.IsSuccess)
                    return updated.Error;
            }

            await Portfolio.Ensure(db, currentUser.UserId, asset.Id, ct);
            await db.SaveChangesAsync(ct);

            if (id is null)
                logger.PayoutCreated(payout.Id, asset.Id, req.Kind);
            else
                logger.PayoutEdited(payout.Id);
            return PayoutResponse.From(payout, asset, await db.AssetLogos.AnyAsync(l => l.AssetId == asset.Id, ct));
        }
    }

    public static void MapCreate(IEndpointRouteBuilder app) =>
        app.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Create(request, ct);
                return result.IsSuccess ? Results.Created((string?)null, result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<PayoutResponse>(201)
            .ProducesValidationProblem()
            .ProducesProblem(404);

    public static void MapUpdate(IEndpointRouteBuilder app) =>
        app.MapPut("/{id:guid}", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Update(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<PayoutResponse>(200)
            .ProducesValidationProblem()
            .ProducesProblem(404);
}
