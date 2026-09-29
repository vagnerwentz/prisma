using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Transactions;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Recurrences;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Recurrences;

public static class UpdateRecurrence
{
    // Todos os campos juntos, como no PATCH da conta. Vale do próximo em diante (docs/fase-2.md, 2.14,
    // regra 6). NextDate: a nova partida da agenda; obrigatória ao trocar a frequência.
    public sealed record Request(
        Guid AccountId,
        long AmountCents,
        Guid? CategoryId,
        string? Description,
        PaymentMethod Method,
        RecurrenceFrequency Frequency,
        DateOnly? NextDate,
        DateOnly? EndDate);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Method).IsInEnum().WithMessage("Meio de pagamento inválido.");
            RuleFor(x => x.Frequency).IsInEnum().WithMessage("Frequência inválida.");
        }
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, RecurrenceRunner runner, ILogger<Handler> logger)
    {
        public async Task<Result<RecurrenceResponse>> Execute(Guid id, Request req, CancellationToken ct)
        {
            var recurrence = await db.Recurrences.SingleOrDefaultAsync(r => r.Id == id, ct);
            if (recurrence is null)
                return new Error(ErrorType.NotFound, "Série não encontrada.");

            var references = await TransactionReferences.Load(db, req.AccountId, req.CategoryId, ct);
            if (!references.IsSuccess)
                return references.Error;

            var (account, category) = references.Value;
            var edited = recurrence.Edit(
                account, req.AmountCents, category, req.Description, req.Method, req.Frequency, req.NextDate, req.EndDate);
            if (!edited.IsSuccess)
                return edited.Error;

            // A tarefa gerando ao mesmo tempo: o xmin da série dá 409 a um dos dois (regra 10).
            await db.SaveChangesAsync(ct);
            logger.RecurrenceEdited(recurrence.Id);

            return await CreateRecurrence.Generated(db, runner, currentUser.UserId, recurrence.Id, ct);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/{id:guid}", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<RecurrenceResponse>(200)
            .ProducesValidationProblem()
            .ProducesProblem(404)
            .ProducesProblem(409);
}
