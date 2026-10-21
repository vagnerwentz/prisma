using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.FixedIncome;
using Prisma.Domain.FixedIncome.Coverage;

namespace Prisma.Api.Features.FixedIncome;

// Uma linha da prévia do lote: um aporte pedido, o saldo dele na data e, se o valor chegou até ele, o resgate exatamente como seria
// gravado. `Skipped` diz por que ficou de fora: `NotYetDeposited` (o aporte é depois da data) ou `FullyRedeemed` (sem principal).
public sealed record BatchLine(
    Guid DepositId, long? BalanceCents, long? RemainingPrincipalCents, string? Skipped, RedemptionResponse? Redemption);

public sealed record BatchTotals(long GrossCents, long PrincipalCents, long YieldCents, long TaxCents, long NetCents);

// A prévia do resgate em lote, sem gravar nada (docs/renda-fixa-resgate.md, 15). Pedido que o domínio recusaria vem como `Problem` (e
// `ProblemCode`), não como erro HTTP, como na prévia de um aporte só. `TotalBalanceCents` é a soma dos saldos que podem sair (o "tudo").
public sealed record BatchPreviewResponse(
    DateOnly Date, long TotalBalanceCents, IReadOnlyList<BatchLine> Lines, BatchTotals? Totals, string? Problem, string? ProblemCode,
    DateOnly? LatestDate = null);

// Os resgates criados, um por aporte, e o total do lote.
public sealed record BatchResponse(IReadOnlyList<RedemptionResponse> Redemptions, BatchTotals Totals);

// Resgatar vários aportes de uma vez (docs/renda-fixa-resgate.md, 15). Cada aporte mantém o seu `FixedIncomeRedemption`: o lote só os
// cria juntos, tudo ou nada. Quem divide o valor é o `RedemptionAllocator`; principal, rendimento e imposto de cada pedaço são do
// `FixedIncomeRedemption.Create`, a mesma regra do resgate de um aporte só.
public static class RedemptionBatches
{
    public const int MaxDeposits = 100;

    // `DepositIds` são os aportes do lote (a tela resolve "todos do objetivo" ou a seleção); `All` resgata o saldo inteiro deles na data,
    // ou `GrossCents` é o valor bruto, dividido do aporte mais antigo ao mais novo. NetCents é o líquido do extrato, um só para o lote.
    // Funding é opcional: a conta onde o dinheiro caiu, uma só; aporte que não saiu de uma conta pelo app entra só como registro.
    public sealed record CreateRequest(
        Guid ClientRequestId, IReadOnlyList<Guid> DepositIds, DateOnly RedemptionDate, bool All = false, long? GrossCents = null,
        long? NetCents = null, Redemptions.FundingRequest? Funding = null);

    public sealed record UndoItem(Guid DepositId, Guid RedemptionId);

    // `Transfer` é o mesmo "manter ou excluir junto" do resgate de um aporte só: `keep` ou `delete`.
    public sealed record UndoRequest(IReadOnlyList<UndoItem> Items, string? Transfer = null);

    public sealed class CreateValidator : AbstractValidator<CreateRequest>
    {
        public CreateValidator()
        {
            RuleFor(x => x.ClientRequestId).NotEmpty().WithMessage("O pedido veio sem identificador.");
            RuleFor(x => x.DepositIds).NotEmpty().WithMessage("Escolha os aportes do resgate.")
                .Must(ids => ids.Count <= MaxDeposits).WithMessage($"O resgate em lote vai até {MaxDeposits} aportes.")
                .Must(ids => ids.Distinct().Count() == ids.Count && ids.All(id => id != Guid.Empty))
                .WithMessage("Os aportes do resgate estão repetidos ou incompletos.");
            RuleFor(x => x).Must(x => x.All ^ x.GrossCents is not null)
                .WithMessage("Informe o valor do resgate ou escolha resgatar tudo.").OverridePropertyName("GrossCents");
            When(x => x.GrossCents is not null, () =>
                RuleFor(x => x.GrossCents!.Value).GreaterThan(0).WithMessage("O valor do resgate deve ser maior que zero."));
            When(x => x.NetCents is not null, () =>
                RuleFor(x => x.NetCents!.Value).GreaterThan(0).WithMessage("O líquido informado deve ser maior que zero."));
            When(x => x.Funding is not null, () =>
                RuleFor(x => x.Funding!.ToAccountId).NotEmpty().WithMessage("Escolha a conta onde o dinheiro caiu."));
        }
    }

    public sealed class UndoValidator : AbstractValidator<UndoRequest>
    {
        public UndoValidator()
        {
            RuleFor(x => x.Items).NotEmpty().WithMessage("Não há resgates para desfazer.")
                .Must(items => items.Count <= MaxDeposits).WithMessage($"O resgate em lote vai até {MaxDeposits} aportes.")
                .Must(items => items.Select(i => i.DepositId).Distinct().Count() == items.Count)
                .WithMessage("Há dois resgates do mesmo aporte na lista.");
        }
    }

    public sealed class Handler(AppDbContext db, IClock clock, ILogger<Handler> logger)
    {
        // O que a divisão decidiu: uma linha por aporte pedido, o saldo total que pode sair e os resgates como seriam gravados.
        private sealed record Plan(
            IReadOnlyList<BatchLine> Lines, long TotalBalance, IReadOnlyList<(FixedIncomeDeposit Deposit, FixedIncomeRedemption Redemption)> Created,
            Error? Error);

        // O pedido do lote vira um pedido por aporte, sempre o mesmo para o mesmo par: repetir o lote devolve o que já foi criado.
        private static Guid RequestOf(Guid batch, Guid deposit)
        {
            Span<byte> bytes = stackalloc byte[32];
            batch.TryWriteBytes(bytes);
            deposit.TryWriteBytes(bytes[16..]);
            return new Guid(SHA256.HashData(bytes)[..16]);
        }

        public async Task<Result<BatchResponse>> Create(CreateRequest req, CancellationToken ct)
        {
            var derived = req.DepositIds.Select(id => RequestOf(req.ClientRequestId, id)).ToList();

            // O mesmo pedido de novo (toque duplo, rede que repete): devolve os resgates que já existem, sem criar outros. Conta também
            // o excluído, como no resgate de um aporte só.
            if (await Existing(derived, ct) is { Count: > 0 } again)
                return await Respond(again, ct);

            var deposits = await Load(req.DepositIds, track: true, ct);
            if (!deposits.IsSuccess)
                return deposits.Error;

            var previous = await Previous(req.DepositIds, track: true, ct);
            var book = await RateBook.Load(db, clock, ct);
            var plan = BuildPlan(deposits.Value, previous, book, req.RedemptionDate, req.All, req.GrossCents, req.NetCents,
                id => RequestOf(req.ClientRequestId, id));
            if (plan.Error is { } refused)
                return refused;

            if (req.Funding is { } funding)
            {
                if (!await db.Accounts.AnyAsync(a => a.Id == funding.ToAccountId, ct))
                    return new Error(ErrorType.NotFound, "Conta não encontrada.");

                foreach (var (deposit, redemption) in plan.Created)
                {
                    // Aporte que não saiu de uma conta pelo app não tem de onde devolver o principal: entra só como registro.
                    if (deposit.TransferPairId is null)
                        continue;

                    var legs = await Redemptions.Handler.Fund(db, deposit, redemption, funding, ct);
                    if (!legs.IsSuccess)
                    {
                        if (legs.Error.Code == "RedemptionNeedsFundedDeposit")
                            continue;
                        return legs.Error;
                    }

                    // Cada transferência e receita vão na mesma gravação dos resgates: ou sai tudo do lote, ou nada.
                    if (legs.Value.Transfer is { } transfer)
                        db.Transactions.AddRange(transfer.Out, transfer.In);
                    if (legs.Value.Income is { } income)
                        db.Transactions.Add(income);
                }
            }

            foreach (var (deposit, redemption) in plan.Created)
            {
                Touch(deposit);
                db.FixedIncomeRedemptions.Add(redemption);
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            // Dois pedidos iguais ao mesmo tempo: o índice único deixa passar um só, e o outro devolve o que o primeiro criou.
            catch (DbUpdateException ex) when (ex.IsUniqueViolation(FixedIncomeRedemptionConfiguration.RequestIndex))
            {
                db.ChangeTracker.Clear();
                if (await Existing(derived, ct) is { Count: > 0 } raced)
                    return await Respond(raced, ct);
                return new Error(ErrorType.Conflict, "Não foi possível registrar o resgate. Tente de novo.");
            }

            logger.RedemptionBatchAdded(plan.Created.Count);
            return await Respond(plan.Created.Select(c => c.Redemption).ToList(), ct);
        }

        public async Task<Result<BatchPreviewResponse>> Preview(
            IReadOnlyList<Guid> depositIds, DateOnly? dateRequest, bool all, long? grossCents, long? netCents, CancellationToken ct)
        {
            if (depositIds.Count == 0 || depositIds.Count > MaxDeposits || depositIds.Distinct().Count() != depositIds.Count)
                return new Error(ErrorType.Validation, "Escolha os aportes do resgate.");

            var deposits = await Load(depositIds, track: false, ct);
            if (!deposits.IsSuccess)
                return deposits.Error;

            var date = dateRequest ?? clock.Today;
            var previous = await Previous(depositIds, track: false, ct);
            var book = await RateBook.Load(db, clock, ct);
            DateOnly? latest = !deposits.Value.Any(d => d.Remuneration.UsesCdi) ? clock.Today
                : book.History.LastDate is { } lastRate ? ReferenceRateCoverage.LatestRedemptionDate(lastRate, clock.Today) : null;

            var plan = BuildPlan(deposits.Value, previous, book, date, all, grossCents, netCents, _ => Guid.NewGuid());
            var totals = plan.Created.Count == 0 ? null : TotalsOf(plan.Created.Select(c => c.Redemption));
            return new BatchPreviewResponse(date, plan.TotalBalance, plan.Lines, totals, plan.Error?.Message, plan.Error?.Code, latest);
        }

        // Desfaz o lote: exclui o último resgate de cada aporte da lista, tudo ou nada. Se um deles já não é o último (o aporte teve outro
        // resgate depois), nada muda e a resposta diz de qual aporte é.
        public async Task<Result<int>> Undo(UndoRequest req, CancellationToken ct)
        {
            if (req.Transfer is not (null or Redemptions.Keep or Redemptions.DeleteTogether))
                return new Error(ErrorType.Validation, "Escolha manter ou excluir os lançamentos do resgate.");

            var depositIds = req.Items.Select(i => i.DepositId).ToList();
            var deposits = await db.FixedIncomeDeposits.Where(d => depositIds.Contains(d.Id)).ToListAsync(ct);
            var live = (await db.FixedIncomeRedemptions.Where(r => depositIds.Contains(r.DepositId)).ToListAsync(ct)).ToLookup(r => r.DepositId);

            var removals = new List<(FixedIncomeDeposit Deposit, FixedIncomeRedemption Redemption, List<Prisma.Domain.Transactions.Transaction> Entries)>();
            foreach (var item in req.Items)
            {
                var deposit = deposits.SingleOrDefault(d => d.Id == item.DepositId);
                var redemption = live[item.DepositId].SingleOrDefault(r => r.Id == item.RedemptionId);
                if (deposit is null || redemption is null)
                    return new Error(ErrorType.NotFound, "Resgate não encontrado.");

                if (redemption.CanBeDeleted(live[item.DepositId]) is { } refused)
                    return new Error(refused.Type, $"Aporte de {deposit.DepositDate:dd/MM/yyyy}: {refused.Message}", refused.Code);

                var entries = await RedemptionFundings.LiveEntries(db, redemption, ct);
                if (entries.Count > 0 && req.Transfer is null)
                    return new Error(
                        ErrorType.Validation,
                        "Este resgate caiu numa conta. Escolha se os lançamentos ficam ou saem junto.",
                        "TransferChoiceRequired");

                removals.Add((deposit, redemption, entries));
            }

            foreach (var (deposit, redemption, entries) in removals)
            {
                if (req.Transfer == Redemptions.DeleteTogether)
                    db.Transactions.RemoveRange(entries);

                Touch(deposit);
                db.FixedIncomeRedemptions.Remove(redemption);
            }

            await db.SaveChangesAsync(ct);
            logger.RedemptionBatchUndone(removals.Count);
            return removals.Count;
        }

        // Todos os aportes pedidos, na ordem em que vieram (é o desempate da divisão); um que não é da pessoa não existe.
        private async Task<Result<IReadOnlyList<FixedIncomeDeposit>>> Load(IReadOnlyList<Guid> ids, bool track, CancellationToken ct)
        {
            var query = db.FixedIncomeDeposits.Where(d => ids.Contains(d.Id));
            var found = await (track ? query : query.AsNoTracking()).ToListAsync(ct);
            if (found.Count != ids.Count)
                return new Error(ErrorType.NotFound, "Aporte não encontrado.");

            IReadOnlyList<FixedIncomeDeposit> ordered = ids.Select(id => found.Single(d => d.Id == id)).ToList();
            return Result<IReadOnlyList<FixedIncomeDeposit>>.Success(ordered);
        }

        private async Task<ILookup<Guid, FixedIncomeRedemption>> Previous(IReadOnlyList<Guid> ids, bool track, CancellationToken ct)
        {
            var query = db.FixedIncomeRedemptions.Where(r => ids.Contains(r.DepositId));
            return (await (track ? query : query.AsNoTracking()).ToListAsync(ct)).ToLookup(r => r.DepositId);
        }

        private Plan BuildPlan(
            IReadOnlyList<FixedIncomeDeposit> deposits, ILookup<Guid, FixedIncomeRedemption> previous, RateBook book, DateOnly date,
            bool all, long? gross, long? net, Func<Guid, Guid> requestOf)
        {
            var lines = new Dictionary<Guid, BatchLine>();
            var candidates = new List<AllocationCandidate>();

            Plan Failed(Error error, long total = 0) => new(Lines(), total, [], error);
            IReadOnlyList<BatchLine> Lines() => deposits.Select(d => lines.GetValueOrDefault(d.Id) ?? new BatchLine(d.Id, null, null, null, null)).ToList();

            if (date > clock.Today)
                return Failed(new Error(ErrorType.Validation, "A data do resgate não pode estar no futuro."));

            foreach (var deposit in deposits)
            {
                if (deposit.DepositDate > date)
                {
                    lines[deposit.Id] = new BatchLine(deposit.Id, null, null, "NotYetDeposited", null);
                    continue;
                }

                var balance = FixedIncomeRedemption.BalanceOn(deposit, previous[deposit.Id].ToList(), book.History, date, clock.Today);
                if (!balance.IsSuccess)
                    return Failed(Prefixed(deposit, balance.Error));

                var remaining = balance.Value.Principal.Cents;
                var grossBalance = balance.Value.GrossBalance.Cents;
                lines[deposit.Id] = new BatchLine(deposit.Id, grossBalance, remaining, remaining < 1 ? "FullyRedeemed" : null, null);
                if (remaining >= 1 && grossBalance > 0)
                    candidates.Add(new AllocationCandidate(deposit.Id, deposit.DepositDate, grossBalance));
            }

            var total = RedemptionAllocator.TotalBalance(candidates);

            // Sem valor e sem "tudo": só os saldos, para a tela mostrar o que dá para resgatar.
            if (!all && gross is null)
                return new Plan(Lines(), total, [], null);

            var parts = RedemptionAllocator.Allocate(candidates, all ? total : gross!.Value);
            if (!parts.IsSuccess)
                return Failed(parts.Error, total);

            var created = new List<(FixedIncomeDeposit, FixedIncomeRedemption)>();
            foreach (var part in parts.Value)
            {
                var deposit = deposits.Single(d => d.Id == part.DepositId);
                var redemption = FixedIncomeRedemption.Create(
                    deposit, previous[deposit.Id].ToList(), book.History, date, part.GrossCents, null, requestOf(deposit.Id), clock.Today);
                if (!redemption.IsSuccess)
                    return Failed(Prefixed(deposit, redemption.Error), total);

                created.Add((deposit, redemption.Value));
            }

            // O líquido do extrato é um só: reparte-se o imposto entre os aportes e cada resgate é refeito com o líquido que lhe coube.
            if (net is { } statementNet)
            {
                var split = RedemptionAllocator.DistributeNet(
                    created.Select(c => new NetLine(c.Item1.Id, c.Item2.GrossCents, c.Item2.YieldCents, c.Item2.TaxCents)).ToList(), statementNet);
                if (!split.IsSuccess)
                    return Failed(split.Error, total);

                for (var i = 0; i < created.Count; i++)
                {
                    var deposit = created[i].Item1;
                    var redemption = FixedIncomeRedemption.Create(
                        deposit, previous[deposit.Id].ToList(), book.History, date, created[i].Item2.GrossCents, split.Value[i].NetCents,
                        requestOf(deposit.Id), clock.Today);
                    if (!redemption.IsSuccess)
                        return Failed(Prefixed(deposit, redemption.Error), total);

                    created[i] = (deposit, redemption.Value);
                }
            }

            foreach (var (deposit, redemption) in created)
                lines[deposit.Id] = lines[deposit.Id] with { Redemption = RedemptionResponse.From(redemption) };

            return new Plan(Lines(), total, created, null);
        }

        // O erro que fala de um aporte só (datas, resgate anterior) diz de qual; o das taxas e o dos valores falam do lote todo.
        private static Error Prefixed(FixedIncomeDeposit deposit, Error error) =>
            error.Code is null ? new Error(error.Type, $"Aporte de {deposit.DepositDate:dd/MM/yyyy}: {error.Message}") : error;

        private static BatchTotals TotalsOf(IEnumerable<FixedIncomeRedemption> redemptions)
        {
            var list = redemptions.ToList();
            return new BatchTotals(
                list.Sum(r => r.GrossCents), list.Sum(r => r.PrincipalCents), list.Sum(r => r.YieldCents), list.Sum(r => r.TaxCents),
                list.Sum(r => r.NetCents));
        }

        private async Task<Result<BatchResponse>> Respond(IReadOnlyCollection<FixedIncomeRedemption> redemptions, CancellationToken ct)
        {
            var ordered = redemptions.OrderBy(r => r.RedemptionDate).ThenBy(r => r.DepositId).ToList();
            return new BatchResponse(await RedemptionFundings.Respond(db, ordered, ct), TotalsOf(ordered));
        }

        private async Task<List<FixedIncomeRedemption>> Existing(IReadOnlyList<Guid> requests, CancellationToken ct) =>
            await db.FixedIncomeRedemptions.IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).AsNoTracking()
                .Where(r => requests.Contains(r.ClientRequestId)).ToListAsync(ct);

        // Muda o aporte sem mudar o contrato dele: o token de concorrência (xmin) faz o segundo de dois pedidos simultâneos dar 409.
        private void Touch(FixedIncomeDeposit deposit) => db.Entry(deposit).Property(d => d.UpdatedAt).IsModified = true;
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/redemptions/batch", async (CreateRequest request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Create(request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<CreateRequest>>()
            .Produces<BatchResponse>(200)
            .ProducesValidationProblem()
            .ProducesProblem(404)
            .ProducesProblem(409);

        app.MapGet("/redemptions/batch/preview",
                async (Guid[] depositIds, DateOnly? date, bool? all, long? grossCents, long? netCents, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Preview(depositIds, date, all ?? false, grossCents, netCents, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<BatchPreviewResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(404);

        app.MapPost("/redemptions/batch/undo", async (UndoRequest request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Undo(request, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<UndoRequest>>()
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);
    }
}
