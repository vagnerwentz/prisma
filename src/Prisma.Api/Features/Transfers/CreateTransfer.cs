using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Transactions;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Transfers;

public static class CreateTransfer
{
    // Transferência livre entre contas (docs/fase-1.md, 2.3). Cartão só recebe pelo pagamento
    // da fatura (POST /statements/{id}/pay).
    public sealed record Request(
        Guid FromAccountId,
        Guid ToAccountId,
        long AmountCents,
        DateOnly Date,
        PaymentMethod Method,
        string? Description);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Method).IsInEnum().WithMessage("Meio de pagamento inválido.");
            RuleFor(x => x.Date).NotEmpty().WithMessage("Informe a data.");
        }
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser)
    {
        public async Task<Result<IReadOnlyList<TransactionResponse>>> Execute(Request req, CancellationToken ct)
        {
            // Os filtros globais garantem que as contas são do usuário.
            var accounts = await db.Accounts
                .Where(a => a.Id == req.FromAccountId || a.Id == req.ToAccountId)
                .ToListAsync(ct);
            var from = accounts.SingleOrDefault(a => a.Id == req.FromAccountId);
            var to = accounts.SingleOrDefault(a => a.Id == req.ToAccountId);
            if (from is null || to is null)
                return new Error(ErrorType.Validation, "Conta não encontrada.");

            var legs = Transfer.Create(currentUser.UserId, from, to, req.AmountCents, req.Date, req.Method, req.Description);
            if (!legs.IsSuccess)
                return legs.Error;

            db.Transactions.AddRange(legs.Value.Out, legs.Value.In);
            await db.SaveChangesAsync(ct);
            return new[] { TransactionResponse.From(legs.Value.Out), TransactionResponse.From(legs.Value.In) };
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/transfers", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess ? Results.Created("/transactions", result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<IReadOnlyList<TransactionResponse>>(201)
            .ProducesValidationProblem();
}
