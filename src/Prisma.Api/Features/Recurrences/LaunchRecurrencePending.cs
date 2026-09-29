using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Statements;
using Prisma.Api.Features.Transactions;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Categories;
using Prisma.Domain.Recurrences;

namespace Prisma.Api.Features.Recurrences;

public static class LaunchRecurrencePending
{
    // Lança a cobrança pendente (docs/fase-2.md, 2.14, regra 9): na fatura seguinte à paga, presa; ou na
    // própria, depois de a pessoa desfazer o pagamento. A pendência sai; a data da cobrança é a do dia dela.
    public sealed record Request(PendingLaunch Where);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() =>
            RuleFor(x => x.Where).IsInEnum().WithMessage("Destino inválido: use NextStatement ou SameStatement.");
    }

    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<TransactionResponse>> Execute(Guid id, Request req, CancellationToken ct)
        {
            try
            {
                return await ConcurrentStatementOpening.Retry(db, () => Launch(id, req, ct));
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation(TransactionConfiguration.RecurrenceOccurrenceIndex))
            {
                // Dois toques ao mesmo tempo: o índice da ocorrência deixa passar um lançamento só.
                return AlreadyResolved;
            }
        }

        private async Task<Result<TransactionResponse>> Launch(Guid id, Request req, CancellationToken ct)
        {
            var pending = await db.RecurrencePendings.SingleOrDefaultAsync(p => p.Id == id, ct);
            if (pending is null)
                return new Error(ErrorType.NotFound, "Pendência não encontrada.");

            var recurrence = await db.Recurrences.SingleAsync(r => r.Id == pending.RecurrenceId, ct);
            var card = await db.Accounts.SingleOrDefaultAsync(a => a.Id == pending.AccountId, ct);
            if (card is null)
                return new Error(ErrorType.Conflict, "O cartão desta cobrança foi excluído.");

            // Categoria excluída: o filtro não a traz, e a cobrança sai sem categoria (regra 8).
            Category? category = recurrence.CategoryId is { } categoryId
                ? await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, ct)
                : null;
            var statements = await db.Statements.Where(s => s.AccountId == card.Id).ToListAsync(ct);

            var launched = recurrence.LaunchPending(pending, card, category, statements, req.Where);
            if (!launched.IsSuccess)
                return launched.Error;

            var charge = launched.Value.Installments[0];
            db.Statements.AddRange(launched.Value.OpenedStatements);
            db.Transactions.Add(charge);
            db.RecurrencePendings.Remove(pending);
            // Pagar a fatura ao mesmo tempo: um dos dois recebe 409 (2.9, regra 6).
            StatementTouch.Touch(db, statements.Where(s => s.Id == charge.StatementId));
            await db.SaveChangesAsync(ct);

            logger.RecurrencePendingLaunched(pending.Id, req.Where);
            return TransactionResponse.From(charge);
        }
    }

    internal static readonly Error AlreadyResolved = new(ErrorType.Conflict, "Esta cobrança já foi resolvida.");

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/pendings/{id:guid}/launch", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Created((string?)null, result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<TransactionResponse>(201)
            .ProducesValidationProblem()
            .ProducesProblem(404)
            .ProducesProblem(409);
}
