using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Transactions;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Statements;

public static class PayStatement
{
    // Paga o total da fatura a partir de uma conta (docs/fase-1.md, 2.3).
    public sealed record Request(Guid FromAccountId, DateOnly Date, PaymentMethod Method, string? Description);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Method).IsInEnum().WithMessage("Meio de pagamento inválido.");
            RuleFor(x => x.Date).NotEmpty().WithMessage("Informe a data do pagamento.");
        }
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser)
    {
        public async Task<Result<IReadOnlyList<TransactionResponse>>> Execute(Guid id, Request req, CancellationToken ct)
        {
            var statement = await db.Statements.SingleOrDefaultAsync(s => s.Id == id, ct);
            if (statement is null)
                return new Error(ErrorType.NotFound, "Fatura não encontrada.");

            var card = await db.Accounts.SingleOrDefaultAsync(a => a.Id == statement.AccountId, ct);
            if (card is null)
                return new Error(ErrorType.Conflict, "O cartão desta fatura foi excluído.");

            var from = await db.Accounts.SingleOrDefaultAsync(a => a.Id == req.FromAccountId, ct);
            if (from is null)
                return new Error(ErrorType.Validation, "Conta não encontrada.");

            var total = await StatementTotals.Of(db, statement.Id, ct);
            var legs = Transfer.PayStatement(currentUser.UserId, statement, card, from, total, req.Date, req.Method, req.Description);
            if (!legs.IsSuccess)
                return legs.Error;

            db.Transactions.AddRange(legs.Value.Out, legs.Value.In);
            await db.SaveChangesAsync(ct);
            return new[] { TransactionResponse.From(legs.Value.Out), TransactionResponse.From(legs.Value.In) };
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/statements/{id:guid}/pay", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Created("/transactions", result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<IReadOnlyList<TransactionResponse>>(201)
            .ProducesValidationProblem()
            .ProducesProblem(404)
            .ProducesProblem(409);
}
