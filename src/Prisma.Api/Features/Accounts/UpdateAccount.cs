using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Accounts;

public static class UpdateAccount
{
    // Todos os campos editáveis vêm juntos: evita a ambiguidade de "null" entre "não enviado"
    // e "limpar o valor" (ex.: remover o limite do cartão). O tipo da conta não é editável.
    public sealed record Request(
        string Name,
        long InitialBalanceCents,
        int? ClosingDay,
        int? DueDay,
        long? CreditLimitCents,
        bool IsActive);

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<AccountResponse>> Execute(Guid id, Request req, CancellationToken ct)
        {
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id, ct);
            if (account is null)
                return new Error(ErrorType.NotFound, "Conta não encontrada.");

            var updated = account.Update(
                req.Name, req.InitialBalanceCents, req.ClosingDay, req.DueDay, req.CreditLimitCents, req.IsActive);
            if (!updated.IsSuccess)
                return updated.Error;

            await db.SaveChangesAsync(ct);
            return AccountResponse.From(account);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/{id:guid}", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(id, request, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<AccountResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(404);
}
