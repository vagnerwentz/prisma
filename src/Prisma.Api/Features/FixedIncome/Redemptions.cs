using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Categories;
using Prisma.Domain.FixedIncome;
using Prisma.Domain.FixedIncome.Coverage;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.FixedIncome;

// Um resgate já gravado: o que saiu (bruto, principal e rendimento), o imposto e o líquido. Tudo congelado na gravação, nunca recalculado
// (docs/renda-fixa-resgate.md, 2). IOF e IR vêm nulos quando a pessoa informou o líquido do extrato (TaxSource = Informed).
public sealed record RedemptionResponse(
    Guid Id, Guid DepositId, DateOnly RedemptionDate, long GrossCents, long PrincipalCents, long YieldCents, long? IofCents, long? IrCents,
    long TaxCents, long NetCents, RedemptionTaxSource TaxSource, string? TaxRuleVersion, RedemptionFundingResponse? Funding = null)
{
    public static RedemptionResponse From(FixedIncomeRedemption r, RedemptionFundingResponse? funding = null) =>
        new(r.Id, r.DepositId, r.RedemptionDate, r.GrossCents, r.PrincipalCents, r.YieldCents, r.IofCents, r.IrCents, r.TaxCents,
            r.NetCents, r.TaxSource, r.TaxRuleVersion, funding);
}

// Os lançamentos que o resgate fez nas contas, quando continuam vivos: a transferência do principal (da conta de investimento para a de
// destino) e a receita do rendimento líquido. Sem nenhum vivo, o resgate aparece sem `Funding`.
public sealed record RedemptionFundingResponse(
    Guid? TransferPairId, Guid? IncomeTransactionId, Guid? InvestmentAccountId, Guid DestinationAccountId);

// A prévia de um resgate, sem gravar nada (docs/renda-fixa-resgate.md, 6): o saldo do aporte na data, o que sobra de principal e, se o valor
// veio, o resgate exatamente como seria gravado (principal, rendimento, imposto, líquido). O imposto é do domínio, nunca refeito na tela.
// Pedido que o domínio recusaria vem como `Problem` (e `ProblemCode`), não como erro HTTP: a tela mostra o motivo enquanto a pessoa digita.
// Sem saldo calculável (data que não serve), `BalanceCents` é nulo. `LatestDate` é o último dia em que o resgate é possível hoje (no CDI, o
// primeiro dia útil depois da última taxa; no pré-fixado, hoje): a tela parte dele em vez de pedir um "hoje" que ainda não vale.
public sealed record RedemptionPreviewResponse(
    DateOnly Date, long? BalanceCents, long? RemainingPrincipalCents, RedemptionResponse? Redemption, string? Problem, string? ProblemCode,
    DateOnly? LatestDate = null);

// Resgatar um aporte, listar, excluir e desfazer (docs/renda-fixa-resgate.md). O domínio decide o que vale (valor, datas, imposto);
// aqui se carrega o aporte, as taxas e os resgates anteriores, e se garante que o mesmo pedido repetido não cria dois resgates e que dois
// resgates ao mesmo tempo não vendem o mesmo saldo duas vezes (o aporte leva o token de concorrência: o segundo pedido dá 409).
public static class Redemptions
{
    public const string Keep = "keep";
    public const string DeleteTogether = "delete";

    // O pedido de resgate. O ClientRequestId vem da tela, escolhido ao abrir o painel. NetCents é o líquido do extrato, opcional.
    //
    // Funding é opcional: em que conta o dinheiro caiu. Só vale para aporte que saiu de uma conta pelo app, que diz de qual conta de
    // investimento o principal volta; com ele, o app cria a transferência do principal e a receita do rendimento líquido na mesma gravação.
    public sealed record FundingRequest(Guid ToAccountId);

    public sealed record CreateRequest(
        Guid ClientRequestId, DateOnly RedemptionDate, long GrossCents, long? NetCents = null, FundingRequest? Funding = null);

    public sealed class CreateValidator : AbstractValidator<CreateRequest>
    {
        public CreateValidator()
        {
            RuleFor(x => x.ClientRequestId).NotEmpty().WithMessage("O pedido veio sem identificador.");
            RuleFor(x => x.GrossCents).GreaterThan(0).WithMessage("O valor do resgate deve ser maior que zero.");
            When(x => x.Funding is not null, () =>
                RuleFor(x => x.Funding!.ToAccountId).NotEmpty().WithMessage("Escolha a conta onde o dinheiro caiu."));
        }
    }

    public sealed class Handler(AppDbContext db, IClock clock, ILogger<Handler> logger)
    {
        public async Task<Result<RedemptionResponse>> Create(Guid depositId, CreateRequest req, CancellationToken ct)
        {
            var deposit = await db.FixedIncomeDeposits.SingleOrDefaultAsync(d => d.Id == depositId, ct);
            if (deposit is null)
                return new Error(ErrorType.NotFound, "Aporte não encontrado.");

            // O mesmo pedido de novo (toque duplo, rede que repete): devolve o resgate que já existe, sem criar outro.
            if (await Existing(depositId, req.ClientRequestId, ct) is { } again)
                return again;

            var previous = await db.FixedIncomeRedemptions.Where(r => r.DepositId == depositId).ToListAsync(ct);
            var book = await RateBook.Load(db, clock, ct);
            var created = FixedIncomeRedemption.Create(
                deposit, previous, book.History, req.RedemptionDate, req.GrossCents, req.NetCents, req.ClientRequestId, clock.Today);
            if (!created.IsSuccess)
                return created.Error;

            if (req.Funding is { } funding)
            {
                var legs = await Fund(db, deposit, created.Value, funding, ct);
                if (!legs.IsSuccess)
                    return legs.Error;

                // A transferência, a receita e o resgate vão na mesma gravação: ou saem os três, ou nenhum.
                if (legs.Value.Transfer is { } transfer)
                    db.Transactions.AddRange(transfer.Out, transfer.In);
                if (legs.Value.Income is { } income)
                    db.Transactions.Add(income);
            }

            Touch(deposit);
            db.FixedIncomeRedemptions.Add(created.Value);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            // Dois pedidos iguais ao mesmo tempo: o índice único deixa passar só um, e o outro devolve o que o primeiro criou.
            catch (DbUpdateException ex) when (ex.IsUniqueViolation(FixedIncomeRedemptionConfiguration.RequestIndex))
            {
                db.ChangeTracker.Clear();
                if (await Existing(depositId, req.ClientRequestId, ct) is { } raced)
                    return raced;
                return new Error(ErrorType.Conflict, "Não foi possível registrar o resgate. Tente de novo.");
            }

            logger.RedemptionAdded(created.Value.Id, depositId);
            return await RedemptionFundings.Respond(db, created.Value, ct);
        }

        // As pontas do resgate numa conta (docs/renda-fixa-resgate.md, 5): só um aporte que saiu de uma conta pelo app sabe de qual conta de
        // investimento o principal volta. Sem isso (aporte só acompanhado, ou com as pontas apagadas), a conta é recusada.
        internal static async Task<Result<RedemptionLegs>> Fund(
            AppDbContext db, FixedIncomeDeposit deposit, FixedIncomeRedemption redemption, FundingRequest funding, CancellationToken ct)
        {
            var depositLegs = deposit.TransferPairId is { } pair ? await Fundings.LiveLegs(db, pair, ct) : [];
            var investmentId = depositLegs.Count == 2
                ? depositLegs.SingleOrDefault(t => t.TransferDirection == TransferDirection.In)?.AccountId
                : null;
            var investment = investmentId is { } id ? await db.Accounts.SingleOrDefaultAsync(a => a.Id == id, ct) : null;
            if (investment is null)
                return new Error(
                    ErrorType.Validation,
                    "Esse aporte não saiu de uma conta pelo app, então não há de onde devolver o principal. Registre só o resgate.",
                    "RedemptionNeedsFundedDeposit");

            var destination = await db.Accounts.SingleOrDefaultAsync(a => a.Id == funding.ToAccountId, ct);
            if (destination is null)
                return new Error(ErrorType.NotFound, "Conta não encontrada.");

            // Nulo se a pessoa apagou a categoria padrão: a receita fica sem categoria, como o provento.
            var category = await db.Categories.FirstOrDefaultAsync(c => c.TemplateKey == DefaultCategories.InvestmentIncomeKey, ct);
            return FixedIncomeRedemptionFunding.Create(deposit.UserId, investment, destination, redemption, category);
        }

        public async Task<Result<RedemptionPreviewResponse>> Preview(
            Guid depositId, DateOnly? dateRequest, long? grossCents, long? netCents, CancellationToken ct)
        {
            var deposit = await db.FixedIncomeDeposits.AsNoTracking().SingleOrDefaultAsync(d => d.Id == depositId, ct);
            if (deposit is null)
                return new Error(ErrorType.NotFound, "Aporte não encontrado.");

            var date = dateRequest ?? clock.Today;
            var previous = await db.FixedIncomeRedemptions.AsNoTracking().Where(r => r.DepositId == depositId).ToListAsync(ct);
            var book = await RateBook.Load(db, clock, ct);
            DateOnly? latest = !deposit.Remuneration.UsesCdi ? clock.Today
                : book.History.LastDate is { } lastRate ? ReferenceRateCoverage.LatestRedemptionDate(lastRate, clock.Today) : null;
            var balance = FixedIncomeRedemption.BalanceOn(deposit, previous, book.History, date, clock.Today);
            if (!balance.IsSuccess)
                return new RedemptionPreviewResponse(date, null, null, null, balance.Error.Message, balance.Error.Code, latest);

            var remaining = balance.Value.Principal.Cents;
            var gross = balance.Value.GrossBalance.Cents;
            if (grossCents is not { } wanted)
                return new RedemptionPreviewResponse(
                    date, gross, remaining, null, remaining < 1 ? "Esse aporte já foi resgatado por inteiro." : null, null, latest);

            var created = FixedIncomeRedemption.Create(deposit, previous, book.History, date, wanted, netCents, Guid.NewGuid(), clock.Today);
            return created.IsSuccess
                ? new RedemptionPreviewResponse(date, gross, remaining, RedemptionResponse.From(created.Value), null, null, latest)
                : new RedemptionPreviewResponse(date, gross, remaining, null, created.Error.Message, created.Error.Code, latest);
        }

        public async Task<Result<IReadOnlyList<RedemptionResponse>>> List(Guid depositId, CancellationToken ct)
        {
            if (!await db.FixedIncomeDeposits.AnyAsync(d => d.Id == depositId, ct))
                return new Error(ErrorType.NotFound, "Aporte não encontrado.");

            var rows = await db.FixedIncomeRedemptions.AsNoTracking().Where(r => r.DepositId == depositId).ToListAsync(ct);
            return Result<IReadOnlyList<RedemptionResponse>>.Success(
                await RedemptionFundings.Respond(db, FixedIncomeRedemption.InOrder(rows), ct));
        }

        public async Task<Result<Guid>> Delete(Guid depositId, Guid id, string? transfer, CancellationToken ct)
        {
            if (transfer is not (null or Keep or DeleteTogether))
                return new Error(ErrorType.Validation, "Escolha manter ou excluir os lançamentos do resgate.");

            var deposit = await db.FixedIncomeDeposits.SingleOrDefaultAsync(d => d.Id == depositId, ct);
            var live = deposit is null ? [] : await db.FixedIncomeRedemptions.Where(r => r.DepositId == depositId).ToListAsync(ct);
            var redemption = live.SingleOrDefault(r => r.Id == id);
            if (deposit is null || redemption is null)
                return new Error(ErrorType.NotFound, "Resgate não encontrado.");
            if (redemption.CanBeDeleted(live) is { } refused)
                return refused;

            // Os lançamentos nas contas existem à parte, e o que fazer com eles é de quem exclui (como no aporte financiado):
            //   ?transfer=keep    só o resgate sai; a transferência e a receita ficam nas contas;
            //   ?transfer=delete  saem juntos, no mesmo instante, e o "Desfazer" os traz juntos.
            var entries = await RedemptionFundings.LiveEntries(db, redemption, ct);
            if (entries.Count > 0 && transfer is null)
                return new Error(
                    ErrorType.Validation,
                    "Este resgate caiu numa conta. Escolha se os lançamentos ficam ou saem junto.",
                    "TransferChoiceRequired");
            if (transfer == DeleteTogether)
                db.Transactions.RemoveRange(entries);

            Touch(deposit);
            db.FixedIncomeRedemptions.Remove(redemption);
            await db.SaveChangesAsync(ct);
            logger.RedemptionDeleted(id, depositId);
            return id;
        }

        public async Task<Result<RedemptionResponse>> Restore(Guid depositId, Guid id, CancellationToken ct)
        {
            // Regra 6: ignora só o soft delete; o filtro de dono continua.
            var deposit = await db.FixedIncomeDeposits.SingleOrDefaultAsync(d => d.Id == depositId, ct);
            var all = deposit is null
                ? []
                : await db.FixedIncomeRedemptions.IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                    .Where(r => r.DepositId == depositId).ToListAsync(ct);
            var redemption = all.SingleOrDefault(r => r.Id == id);
            if (deposit is null || redemption is null)
                return new Error(ErrorType.NotFound, "Resgate não encontrado.");
            if (redemption.DeletedAt is null)
                return await RedemptionFundings.Respond(db, redemption, ct);

            var deletedAt = redemption.DeletedAt;
            if (redemption.Restore(all.Where(r => r.Id != id)) is { } refused)
                return refused;

            // Os lançamentos que saíram junto com o resgate (o soft delete grava o mesmo instante em todos) voltam junto; se uma das contas
            // foi excluída nesse meio-tempo, nada volta.
            var together = await RedemptionFundings.DeletedTogether(db, redemption, deletedAt, ct);
            if (together.Count > 0)
            {
                var accountIds = together.Select(t => t.AccountId).Distinct().ToList();
                if (await db.Accounts.CountAsync(a => accountIds.Contains(a.Id), ct) != accountIds.Count)
                    return new Error(ErrorType.Conflict, "Uma das contas deste resgate foi excluída; não é possível desfazer.");

                var categoryIds = together.Where(t => t.CategoryId is not null).Select(t => t.CategoryId!.Value).Distinct().ToList();
                var living = await db.Categories.Where(c => categoryIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
                foreach (var entry in together)
                    entry.Restore(categoryStillExists: entry.CategoryId is not { } category || living.Contains(category));
            }

            Touch(deposit);
            await db.SaveChangesAsync(ct);
            logger.RedemptionRestored(id, depositId);
            return await RedemptionFundings.Respond(db, redemption, ct);
        }

        // Muda o aporte sem mudar o contrato dele: o token de concorrência (xmin) faz o segundo de dois pedidos simultâneos dar 409.
        private void Touch(FixedIncomeDeposit deposit) => db.Entry(deposit).Property(d => d.UpdatedAt).IsModified = true;

        private async Task<RedemptionResponse?> Existing(Guid depositId, Guid clientRequestId, CancellationToken ct)
        {
            // Conta também o excluído: o índice único conta, e um pedido repetido nunca ressuscita nem duplica.
            var existing = await db.FixedIncomeRedemptions.IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).AsNoTracking()
                .SingleOrDefaultAsync(r => r.ClientRequestId == clientRequestId && r.DepositId == depositId, ct);
            return existing is null ? null : await RedemptionFundings.Respond(db, existing, ct);
        }
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/{id:guid}/redemptions", async (Guid id, CreateRequest request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Create(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<CreateRequest>>()
            .Produces<RedemptionResponse>(200)
            .ProducesValidationProblem()
            .ProducesProblem(404)
            .ProducesProblem(409);

        app.MapGet("/{id:guid}/redemptions/preview",
                async (Guid id, DateOnly? date, long? grossCents, long? netCents, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Preview(id, date, grossCents, netCents, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<RedemptionPreviewResponse>(200)
            .ProducesProblem(404);

        app.MapGet("/{id:guid}/redemptions", async (Guid id, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.List(id, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<IReadOnlyList<RedemptionResponse>>(200)
            .ProducesProblem(404);

        app.MapDelete("/{id:guid}/redemptions/{redemptionId:guid}", async (Guid id, Guid redemptionId, string? transfer, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Delete(id, redemptionId, transfer, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);

        app.MapPost("/{id:guid}/redemptions/{redemptionId:guid}/restore", async (Guid id, Guid redemptionId, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Restore(id, redemptionId, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<RedemptionResponse>(200)
            .ProducesProblem(404)
            .ProducesProblem(409);
    }
}

// Os lançamentos de um resgate nas contas e como a resposta os mostra.
public static class RedemptionFundings
{
    // As linhas vivas que o resgate criou: as duas pontas da transferência do principal e a receita do rendimento.
    public static async Task<List<Transaction>> LiveEntries(AppDbContext db, FixedIncomeRedemption redemption, CancellationToken ct)
    {
        var pair = redemption.TransferPairId;
        var income = redemption.IncomeTransactionId;
        if (pair is null && income is null)
            return [];

        return await db.Transactions.Where(t => (pair != null && t.TransferPairId == pair) || (income != null && t.Id == income))
            .ToListAsync(ct);
    }

    // As que saíram junto com o resgate (o mesmo instante de exclusão), para o "Desfazer".
    public static async Task<List<Transaction>> DeletedTogether(
        AppDbContext db, FixedIncomeRedemption redemption, DateTime? deletedAt, CancellationToken ct)
    {
        var pair = redemption.TransferPairId;
        var income = redemption.IncomeTransactionId;
        if (deletedAt is null || (pair is null && income is null))
            return [];

        // Excluir uma ponta da transferência exclui as duas no mesmo instante, então elas voltam juntas.
        return await db.Transactions.IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
            .Where(t => t.DeletedAt == deletedAt && ((pair != null && t.TransferPairId == pair) || (income != null && t.Id == income)))
            .ToListAsync(ct);
    }

    public static async Task<RedemptionResponse> Respond(AppDbContext db, FixedIncomeRedemption redemption, CancellationToken ct) =>
        (await Respond(db, [redemption], ct))[0];

    public static async Task<IReadOnlyList<RedemptionResponse>> Respond(
        AppDbContext db, IReadOnlyCollection<FixedIncomeRedemption> redemptions, CancellationToken ct)
    {
        var pairs = redemptions.Where(r => r.TransferPairId is not null).Select(r => r.TransferPairId!.Value).Distinct().ToList();
        var incomes = redemptions.Where(r => r.IncomeTransactionId is not null).Select(r => r.IncomeTransactionId!.Value).Distinct().ToList();
        var live = pairs.Count + incomes.Count == 0
            ? []
            : await db.Transactions.AsNoTracking()
                .Where(t => (t.TransferPairId != null && pairs.Contains(t.TransferPairId.Value)) || incomes.Contains(t.Id))
                .Select(t => new { t.Id, t.AccountId, t.TransferPairId, t.TransferDirection })
                .ToListAsync(ct);

        return redemptions.Select(r =>
        {
            var legs = live.Where(t => r.TransferPairId is { } pair && t.TransferPairId == pair).ToList();
            var from = legs.SingleOrDefault(t => t.TransferDirection == TransferDirection.Out);
            var to = legs.SingleOrDefault(t => t.TransferDirection == TransferDirection.In);
            var income = live.SingleOrDefault(t => r.IncomeTransactionId is { } id && t.Id == id);

            // A transferência só conta com as duas pontas vivas; a receita, sozinha.
            var transferLive = from is not null && to is not null;
            var destination = transferLive ? to!.AccountId : income?.AccountId;
            if (destination is null)
                return RedemptionResponse.From(r);

            return RedemptionResponse.From(r, new RedemptionFundingResponse(
                transferLive ? r.TransferPairId : null, income?.Id, transferLive ? from!.AccountId : null, destination.Value));
        }).ToList();
    }
}
