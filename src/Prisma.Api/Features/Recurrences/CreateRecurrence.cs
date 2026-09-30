using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Recurrences;

namespace Prisma.Api.Features.Recurrences;

public static class CreateRecurrence
{
    // Um lançamento que já existe passa a se repetir e vira a primeira ocorrência (docs/fase-2.md, 2.14,
    // regra 1). O que venceu desde ele é gerado na hora, sem esperar a tarefa.
    public sealed class Validator : AbstractValidator<RecurrenceRequest>
    {
        public Validator() => RuleFor(x => x.Frequency).IsInEnum().WithMessage("Frequência inválida.");
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, RecurrenceRunner runner, ILogger<Handler> logger)
    {
        public async Task<Result<RecurrenceResponse>> Execute(Guid transactionId, RecurrenceRequest req, CancellationToken ct)
        {
            var first = await db.Transactions.SingleOrDefaultAsync(t => t.Id == transactionId, ct);
            if (first is null)
                return new Error(ErrorType.NotFound, "Transação não encontrada.");

            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == first.AccountId, ct);
            if (account is null)
                return new Error(ErrorType.Conflict, "A conta deste lançamento foi excluída.");

            if (req.CheckAutoDebitFields() is { } fieldError)
                return fieldError;

            var recurrence = Recurrence.StartFrom(first, account, req.Frequency, req.EndDate, req.AutoDebitTerms);
            if (!recurrence.IsSuccess)
                return recurrence.Error;

            // Dois toques ao mesmo tempo: a ligação do lançamento confere que ele ainda não tinha série
            // (TransactionConfiguration), e o segundo recebe 409.
            db.Recurrences.Add(recurrence.Value);
            await db.SaveChangesAsync(ct);
            logger.RecurrenceStarted(recurrence.Value.Id, recurrence.Value.Frequency);

            return await Generated(db, runner, currentUser.UserId, recurrence.Value.Id, ct);
        }
    }

    // Gera o que venceu e devolve a série como ficou.
    internal static async Task<RecurrenceResponse> Generated(
        AppDbContext db, RecurrenceRunner runner, Guid userId, Guid recurrenceId, CancellationToken ct)
    {
        await runner.RunForUserAsync(userId, ct);
        var recurrence = await db.Recurrences.AsNoTracking().SingleAsync(r => r.Id == recurrenceId, ct);
        var pendings = await db.RecurrencePendings.AsNoTracking().Where(p => p.RecurrenceId == recurrenceId).ToListAsync(ct);
        return RecurrenceResponse.From(recurrence, pendings);
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/transactions/{id:guid}/recurrence",
                async (Guid id, RecurrenceRequest request, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.Execute(id, request, ct);
                    return result.IsSuccess ? Results.Created((string?)null, result.Value) : result.Error.ToProblem();
                })
            .AddEndpointFilter<ValidationFilter<RecurrenceRequest>>()
            .Produces<RecurrenceResponse>(201)
            .ProducesValidationProblem()
            .ProducesProblem(404)
            .ProducesProblem(409);
}
