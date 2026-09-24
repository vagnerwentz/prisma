using System.Linq.Expressions;
using Prisma.Domain.Accounts;

namespace Prisma.Api.Features.Accounts;

public sealed record AccountResponse(
    Guid Id,
    string Name,
    AccountType Type,
    long InitialBalanceCents,
    int? ClosingDay,
    int? DueDay,
    long? CreditLimitCents,
    bool IsActive)
{
    // Projeção usada nas consultas (traduzida para SQL) e, compilada, após os comandos.
    public static readonly Expression<Func<Account, AccountResponse>> Projection = a => new AccountResponse(
        a.Id, a.Name, a.Type, a.InitialBalanceCents, a.ClosingDay, a.DueDay, a.CreditLimitCents, a.IsActive);

    private static readonly Func<Account, AccountResponse> Compiled = Projection.Compile();

    public static AccountResponse From(Account account) => Compiled(account);
}
