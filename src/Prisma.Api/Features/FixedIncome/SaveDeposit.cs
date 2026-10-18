using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.FixedIncome;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.FixedIncome;

// Lançar e editar um aporte (docs/renda-fixa.md, 2.3). O domínio decide o que vale (valor, percentual, datas); aqui se confere
// a caixa, a data contra o início do histórico do CDI, e se garante que o mesmo pedido repetido não cria dois aportes.
public static class SaveDeposit
{
    // De que conta o dinheiro saiu e em qual conta de investimento entrou (D3). Com isto o app cria a transferência junto com o
    // aporte, na mesma gravação. Sem isto, o aporte só é acompanhado, sem mexer em saldo.
    public sealed record FundingRequest(Guid FromAccountId, Guid ToAccountId, PaymentMethod Method = PaymentMethod.Pix);

    // Pedido de criação. O ClientRequestId vem da tela, escolhido ao abrir o formulário.
    public sealed record CreateRequest(
        Guid? GoalId, Guid ClientRequestId, DateOnly DepositDate, long AmountCents, FixedIncomeProductKind ProductKind,
        FixedIncomeIndexKind IndexKind, long IndexPercentE4, DateOnly? MaturityDate, DepositLiquidity Liquidity, string? Note,
        FundingRequest? Funding = null);

    // Edição do contrato. A caixa muda por PATCH /{id}/goal, não aqui.
    public sealed record UpdateRequest(
        DateOnly DepositDate, long AmountCents, FixedIncomeProductKind ProductKind, FixedIncomeIndexKind IndexKind,
        long IndexPercentE4, DateOnly? MaturityDate, DepositLiquidity Liquidity, string? Note);

    public sealed class CreateValidator : AbstractValidator<CreateRequest>
    {
        public CreateValidator()
        {
            RuleFor(x => x.ClientRequestId).NotEmpty().WithMessage("O pedido veio sem identificador.");
            RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("O valor deve ser maior que zero.");
            When(x => x.Funding is not null, () =>
            {
                RuleFor(x => x.Funding!.FromAccountId).NotEmpty().WithMessage("Escolha a conta de onde o dinheiro saiu.");
                RuleFor(x => x.Funding!.ToAccountId).NotEmpty().WithMessage("Escolha a conta de investimento onde ele entrou.");
                RuleFor(x => x.Funding!.Method).IsInEnum().WithMessage("Meio de pagamento inválido.");
            });
        }
    }

    public sealed class UpdateValidator : AbstractValidator<UpdateRequest>
    {
        public UpdateValidator() => RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("O valor deve ser maior que zero.");
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, IClock clock, ILogger<Handler> logger)
    {
        public async Task<Result<DepositResponse>> Create(CreateRequest req, CancellationToken ct)
        {
            // O mesmo pedido de novo (toque duplo, rede que repete): devolve o aporte que já existe, sem criar outro.
            var again = await Existing(req.ClientRequestId, ct);
            if (again is not null)
                return again;

            if (req.GoalId is { } goalId && !await Goals.Exists(db, goalId, ct))
                return new Error(ErrorType.NotFound, "Objetivo não encontrado.");

            var created = FixedIncomeDeposit.Create(
                currentUser.UserId, req.GoalId, req.DepositDate, req.AmountCents, req.ProductKind, req.IndexKind, req.IndexPercentE4,
                req.MaturityDate, req.Liquidity, req.Note, req.ClientRequestId, clock.Today);
            if (!created.IsSuccess)
                return created.Error;
            if (await BeforeFirstRate(req.DepositDate, ct) is { } early)
                return early;

            if (req.Funding is { } funding)
            {
                var fromAccountId = funding.FromAccountId;
                var toAccountId = funding.ToAccountId;
                var accounts = await db.Accounts.Where(a => a.Id == fromAccountId || a.Id == toAccountId).ToListAsync(ct);
                var from = accounts.SingleOrDefault(a => a.Id == fromAccountId);
                var to = accounts.SingleOrDefault(a => a.Id == toAccountId);
                if (from is null || to is null)
                    return new Error(ErrorType.NotFound, "Conta não encontrada.");

                var legs = FixedIncomeFunding.Create(
                    currentUser.UserId, from, to, req.AmountCents, req.DepositDate, funding.Method);
                if (!legs.IsSuccess)
                    return legs.Error;

                // O aporte e as duas pontas da transferência vão na mesma gravação: ou saem os três, ou nenhum.
                created.Value.LinkFunding(legs.Value.Out.TransferPairId!.Value);
                db.Transactions.AddRange(legs.Value.Out, legs.Value.In);
            }

            db.FixedIncomeDeposits.Add(created.Value);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            // Dois pedidos iguais ao mesmo tempo: o índice único deixa passar só um, e o outro devolve o que o primeiro criou.
            catch (DbUpdateException ex) when (ex.IsUniqueViolation(FixedIncomeDepositConfiguration.RequestIndex))
            {
                db.ChangeTracker.Clear();
                var raced = await Existing(req.ClientRequestId, ct);
                if (raced is not null)
                    return raced;
                return new Error(ErrorType.Conflict, "Não foi possível registrar o aporte. Tente de novo.");
            }

            logger.DepositAdded(created.Value.Id, created.Value.GoalId);
            return await Fundings.Respond(db, created.Value, ct);
        }

        public async Task<Result<DepositResponse>> Update(Guid id, UpdateRequest req, CancellationToken ct)
        {
            var deposit = await db.FixedIncomeDeposits.SingleOrDefaultAsync(d => d.Id == id, ct);
            if (deposit is null)
                return new Error(ErrorType.NotFound, "Aporte não encontrado.");

            // Transferência não se edita (docs/fase-1.md, 2.3): o valor e a data de um aporte que saiu de uma conta só mudam
            // excluindo e lançando de novo. Se as pontas foram apagadas de vez, o aporte volta a ser só acompanhado.
            if (deposit.TransferPairId is { } pair)
            {
                if (!await Fundings.AnyLegExists(db, pair, ct))
                    deposit.Unlink();
                else if (req.DepositDate != deposit.DepositDate || req.AmountCents != deposit.AmountCents)
                    return new Error(
                        ErrorType.Conflict,
                        "O valor e a data de um aporte que saiu de uma conta não mudam. Exclua e lance de novo.",
                        "FundedDepositLocked");
            }

            var before = DepositResponse.From(deposit);
            var error = deposit.Update(
                req.DepositDate, req.AmountCents, req.ProductKind, req.IndexKind, req.IndexPercentE4, req.MaturityDate, req.Liquidity,
                req.Note, clock.Today);
            if (error is not null)
                return error;
            if (await BeforeFirstRate(req.DepositDate, ct) is { } early)
                return early;

            await db.SaveChangesAsync(ct);
            logger.DepositEdited(deposit.Id, ChangedFields(before, DepositResponse.From(deposit)));
            return await Fundings.Respond(db, deposit, ct);
        }

        private async Task<DepositResponse?> Existing(Guid clientRequestId, CancellationToken ct)
        {
            // Conta também o excluído: o índice único conta, e um pedido repetido nunca ressuscita nem duplica.
            var existing = await db.FixedIncomeDeposits.IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                .AsNoTracking()
                .SingleOrDefaultAsync(d => d.ClientRequestId == clientRequestId, ct);
            return existing is null ? null : await Fundings.Respond(db, existing, ct);
        }

        // Sem taxa não há rendimento: uma data antes do primeiro dia do histórico do CDI é recusada, não tratada como zero.
        // Sem nenhuma taxa carregada ainda (a primeira sincronização não rodou), não há o que conferir.
        private async Task<Error?> BeforeFirstRate(DateOnly date, CancellationToken ct)
        {
            var first = await db.ReferenceRates.MinAsync(r => (DateOnly?)r.ReferenceDate, ct);
            return first is { } start && date < start
                ? new Error(
                    ErrorType.Validation,
                    $"O histórico do CDI começa em {start:dd/MM/yyyy}. Escolha uma data a partir dela.",
                    "BeforeFirstRate")
                : null;
        }

        // Só os nomes dos campos que mudaram, nunca os valores (docs/renda-fixa.md, seção 8).
        private static string ChangedFields(DepositResponse before, DepositResponse after)
        {
            var changed = new List<string>();
            if (before.DepositDate != after.DepositDate) changed.Add("date");
            if (before.AmountCents != after.AmountCents) changed.Add("amount");
            if (before.IndexPercentE4 != after.IndexPercentE4) changed.Add("percent");
            if (before.MaturityDate != after.MaturityDate) changed.Add("maturity");
            if (before.Liquidity != after.Liquidity) changed.Add("liquidity");
            if (before.Note != after.Note) changed.Add("note");
            if (before.ProductKind != after.ProductKind || before.IndexKind != after.IndexKind) changed.Add("product");
            return changed.Count == 0 ? "none" : string.Join(',', changed);
        }
    }

    public static void MapCreate(IEndpointRouteBuilder app) =>
        app.MapPost("/", async (CreateRequest request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Create(request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<CreateRequest>>()
            .Produces<DepositResponse>(200)
            .ProducesValidationProblem()
            .ProducesProblem(404)
            .ProducesProblem(409);

    public static void MapUpdate(IEndpointRouteBuilder app) =>
        app.MapPut("/{id:guid}", async (Guid id, UpdateRequest request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Update(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<UpdateRequest>>()
            .Produces<DepositResponse>(200)
            .ProducesValidationProblem()
            .ProducesProblem(404)
            .ProducesProblem(409);
}
