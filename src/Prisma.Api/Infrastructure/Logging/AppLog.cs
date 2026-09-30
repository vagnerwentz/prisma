using Microsoft.AspNetCore.Diagnostics;
using Prisma.Domain.Accounts;
using Prisma.Domain.Statements;

namespace Prisma.Api.Infrastructure.Logging;

// Todos os eventos de log da API, num lugar só (CLAUDE.md, seção 6). Mensagens em inglês; as
// propriedades viram campos filtráveis no JSON de produção (@userId:…, @eventName:LoginFailed).
// Regra 8: nunca valor, descrição, nome, e-mail, senha ou token. Só ids, tipos e motivos; um teste de
// arquitetura recusa parâmetro com nome sensível.
// Ids: 1xxx requisição e infraestrutura, 2xxx acesso, 3xxx dinheiro.
public static partial class AppLog
{
    // O userId vem do escopo da requisição, fora da frase: anônima não vira "(user (null))".
    [LoggerMessage(1000, LogLevel.Information, "HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs} ms")]
    public static partial void RequestFinished(this ILogger logger, string method, string route, int statusCode, double elapsedMs);

    [LoggerMessage(1001, LogLevel.Error, "Unhandled exception in {Method} {Route}")]
    public static partial void UnhandledException(this ILogger logger, Exception exception, string method, string route);

    [LoggerMessage(1002, LogLevel.Warning, "Concurrency conflict in {Method} {Route}: the data changed while saving")]
    public static partial void ConcurrencyConflict(this ILogger logger, string method, string route);

    [LoggerMessage(1003, LogLevel.Information, "Applied {Count} pending database migrations on startup")]
    public static partial void MigrationsApplied(this ILogger logger, int count);

    [LoggerMessage(1004, LogLevel.Warning, "Rate limit hit on {Method} {Route}")]
    public static partial void RateLimited(this ILogger logger, string method, string route);

    // A pilha do navegador vai como exceção (BrowserError), no campo `exception`, fora da frase.
    [LoggerMessage(1005, LogLevel.Warning, "Browser error on {Screen} ({Kind}, version {AppVersion}): {ErrorMessage}")]
    public static partial void ClientError(
        this ILogger logger, BrowserError error, string screen, string kind, string appVersion, string errorMessage);

    [LoggerMessage(2000, LogLevel.Information, "User {UserId} registered")]
    public static partial void UserRegistered(this ILogger logger, Guid userId);

    [LoggerMessage(2001, LogLevel.Warning, "Registration rejected: {Reason}")]
    public static partial void RegistrationRejected(this ILogger logger, string reason);

    [LoggerMessage(2002, LogLevel.Information, "User {UserId} signed in")]
    public static partial void LoginSucceeded(this ILogger logger, Guid userId);

    [LoggerMessage(2003, LogLevel.Warning, "Sign-in failed for user {UserId}: {Reason}")]
    public static partial void LoginFailed(this ILogger logger, Guid userId, string reason);

    [LoggerMessage(2004, LogLevel.Warning, "User {UserId} is locked out after repeated failed sign-ins")]
    public static partial void LoginLockedOut(this ILogger logger, Guid userId);

    [LoggerMessage(2005, LogLevel.Warning, "Sign-in failed: no account with that email")]
    public static partial void LoginUnknownEmail(this ILogger logger);

    [LoggerMessage(3000, LogLevel.Information, "Account {AccountId} created ({AccountType})")]
    public static partial void AccountCreated(this ILogger logger, Guid accountId, AccountType accountType);

    [LoggerMessage(3001, LogLevel.Information, "Statement {StatementId} of card {CardId} paid from account {FromAccountId}")]
    public static partial void StatementPaid(this ILogger logger, Guid statementId, Guid cardId, Guid fromAccountId);

    [LoggerMessage(3002, LogLevel.Information, "Purchase {PurchaseId} moved to the {Direction} statement (pinned: {Pinned})")]
    public static partial void PurchaseMovedToStatement(this ILogger logger, Guid purchaseId, StatementShift direction, bool pinned);

    [LoggerMessage(3003, LogLevel.Information, "Purchase {PurchaseId} moved from card {FromCardId} to card {ToCardId}")]
    public static partial void PurchaseMovedToCard(this ILogger logger, Guid purchaseId, Guid fromCardId, Guid toCardId);

    [LoggerMessage(3004, LogLevel.Information, "Recurrence {RecurrenceId} generated {Created} transactions and {Pending} pending occurrences")]
    public static partial void RecurrenceGenerated(this ILogger logger, Guid recurrenceId, int created, int pending);

    [LoggerMessage(3005, LogLevel.Information, "Recurrence {RecurrenceId} was already generated or changed by another run: {Reason}")]
    public static partial void RecurrenceSkipped(this ILogger logger, Guid recurrenceId, string reason);

    [LoggerMessage(3006, LogLevel.Error, "Recurrence {RecurrenceId} failed to generate; the next run tries again")]
    public static partial void RecurrenceFailed(this ILogger logger, Exception exception, Guid recurrenceId);

    [LoggerMessage(3007, LogLevel.Information, "Recurrence run for {Users} users: {Created} transactions, {Pending} pending, {Failed} failed")]
    public static partial void RecurrenceRunFinished(this ILogger logger, int users, int created, int pending, int failed);

    [LoggerMessage(3008, LogLevel.Information, "Recurrence worker started: runs now and every {IntervalMinutes} minutes")]
    public static partial void RecurrenceWorkerStarted(this ILogger logger, double intervalMinutes);

    [LoggerMessage(3009, LogLevel.Error, "Recurrence run failed; the next run tries again")]
    public static partial void RecurrenceRunFailed(this ILogger logger, Exception exception);

    [LoggerMessage(3010, LogLevel.Information, "Recurrence {RecurrenceId} started ({Frequency})")]
    public static partial void RecurrenceStarted(this ILogger logger, Guid recurrenceId, Prisma.Domain.Recurrences.RecurrenceFrequency frequency);

    [LoggerMessage(3011, LogLevel.Information, "Recurrence {RecurrenceId} edited")]
    public static partial void RecurrenceEdited(this ILogger logger, Guid recurrenceId);

    [LoggerMessage(3012, LogLevel.Information, "Recurrence {RecurrenceId} ended")]
    public static partial void RecurrenceEnded(this ILogger logger, Guid recurrenceId);

    [LoggerMessage(3013, LogLevel.Information, "Recurrence pending {PendingId} launched in the {Where}")]
    public static partial void RecurrencePendingLaunched(this ILogger logger, Guid pendingId, Prisma.Domain.Recurrences.PendingLaunch where);

    [LoggerMessage(3014, LogLevel.Information, "Recurrence pending {PendingId} discarded")]
    public static partial void RecurrencePendingDiscarded(this ILogger logger, Guid pendingId);

    [LoggerMessage(3015, LogLevel.Information, "Transaction {TransactionId} amount confirmed (corrected: {Corrected})")]
    public static partial void AmountConfirmed(this ILogger logger, Guid transactionId, bool corrected);

    // Rota modelo (/statements/{id}/pay), nunca o caminho cru: ele pode levar ids e consulta. Depois de
    // uma exceção, o ASP.NET tira o endpoint do contexto e o guarda no IExceptionHandlerFeature.
    // Sem a barra final que os grupos deixam (/accounts/ → /accounts).
    public static string RouteOf(HttpContext context)
    {
        var endpoint = context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint;
        var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText;
        return route is null ? "(unmatched)" : route.Length > 1 ? route.TrimEnd('/') : route;
    }
}
