using Prisma.Domain.Accounts;
using Prisma.Domain.Statements;

namespace Prisma.Domain.Transactions;

public sealed record TransferLegs(Transaction Out, Transaction In);

// Transferência (docs/fase-1.md, 2.3): duas transações Transfer, uma em cada conta, com o mesmo
// TransferPairId. Não é receita nem despesa. Pagar a fatura é uma transferência para o cartão.
public static class Transfer
{
    public static Result<TransferLegs> Create(
        Guid userId, Account from, Account to, long amountCents, DateOnly date, PaymentMethod method, string? description)
    {
        if (from.Id == to.Id)
            return Invalid("Escolha contas diferentes para a transferência.");

        // O cartão só recebe dinheiro pelo pagamento de uma fatura (decisão da etapa 1.10).
        if (from.Type == AccountType.CreditCard || to.Type == AccountType.CreditCard)
            return Invalid("O cartão de crédito recebe dinheiro só pelo pagamento da fatura.");

        if (Validate(amountCents, method, description) is { } error)
            return error;

        return Legs(userId, from, to, amountCents, date, method, Text(description, "Transferência"), null);
    }

    // Paga o total da fatura e a marca como paga. A ponta de entrada fica ligada à fatura pelo
    // StatementId, mas não conta no total dela, que soma só compras.
    public static Result<TransferLegs> PayStatement(
        Guid userId, Statement statement, Account card, Account from, long statementTotalCents, DateOnly date,
        PaymentMethod method, string? description)
    {
        if (statement.AccountId != card.Id)
            throw new ArgumentException("A fatura não é deste cartão.", nameof(statement));

        if (from.Type == AccountType.CreditCard)
            return Invalid("A fatura é paga a partir de uma conta, não de um cartão.");

        if (statement.IsPaid)
            return AlreadyPaid;

        // Antes do fechamento ainda entram compras, e compra em fatura paga é recusada.
        if (date <= statement.ClosingDate)
            return Invalid($"A fatura só pode ser paga depois do fechamento ({statement.ClosingDate:dd/MM/yyyy}).");

        if (statementTotalCents <= 0)
            return Invalid("Não há valor a pagar nesta fatura.");

        if (Validate(statementTotalCents, method, description) is { } error)
            return error;

        var legs = Legs(userId, from, card, statementTotalCents, date, method, Text(description, "Pagamento de fatura"), statement);
        statement.MarkAsPaid();
        return legs;
    }

    // Excluir a transferência (qualquer ponta) desfaz o pagamento da fatura, se for um.
    public static void Remove(TransferLegs legs, Statement? paidStatement)
    {
        if (paidStatement is not null && legs.In.StatementId == paidStatement.Id)
            paidStatement.MarkAsUnpaid();
    }

    // Restaurar um pagamento volta a marcar a fatura como paga, se ela não foi paga de novo e o
    // total continua igual ao valor pago. Transferência livre sempre pode voltar.
    public static Error? Restore(TransferLegs legs, Statement? paidStatement, long statementTotalCents)
    {
        if (paidStatement is not null)
        {
            if (paidStatement.IsPaid)
                return AlreadyPaid;

            if (statementTotalCents != legs.In.AmountCents)
                return new Error(ErrorType.Conflict, "A fatura mudou desde o pagamento. Pague de novo pelo total atual.");

            paidStatement.MarkAsPaid();
        }

        legs.Out.Restore(categoryStillExists: true);
        legs.In.Restore(categoryStillExists: true);
        return null;
    }

    private static readonly Error AlreadyPaid = new(ErrorType.Conflict, "Esta fatura já está paga.");

    private static Error? Validate(long amountCents, PaymentMethod method, string? description)
    {
        if (amountCents <= 0)
            return Invalid("O valor deve ser maior que zero.");

        if (method == PaymentMethod.Credit)
            return Invalid("Transferência não usa o meio de pagamento crédito.");

        if (description?.Trim().Length > Transaction.DescriptionMaxLength)
            return Invalid($"A descrição deve ter no máximo {Transaction.DescriptionMaxLength} caracteres.");

        return null;
    }

    private static string Text(string? description, string fallback) =>
        string.IsNullOrWhiteSpace(description) ? fallback : description.Trim();

    private static TransferLegs Legs(
        Guid userId, Account from, Account to, long amountCents, DateOnly date, PaymentMethod method, string text,
        Statement? statement)
    {
        var pairId = Guid.CreateVersion7();
        return new TransferLegs(
            Transaction.CreateTransferLeg(userId, from, TransferDirection.Out, pairId, amountCents, date, method, text, null),
            Transaction.CreateTransferLeg(userId, to, TransferDirection.In, pairId, amountCents, date, method, text, statement));
    }

    private static Error Invalid(string message) => new(ErrorType.Validation, message);
}
