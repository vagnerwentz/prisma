using Microsoft.AspNetCore.Diagnostics;
using Prisma.Api.Infrastructure.Email;
using Prisma.Domain.Accounts;
using Prisma.Domain.Statements;

namespace Prisma.Api.Infrastructure.Logging;

// Todos os eventos de log da API, num lugar só (CLAUDE.md, seção 6). Mensagens em inglês; as
// propriedades viram campos filtráveis no JSON de produção (@userId:…, @eventName:LoginFailed).
// Regra 8: nunca valor, descrição, nome, e-mail, senha ou token. Só ids, tipos e motivos; um teste de
// arquitetura recusa parâmetro com nome sensível.
// Ids: 1xxx requisição e infraestrutura, 2xxx acesso, 3xxx dinheiro, 4xxx investimentos e dados de mercado, 5xxx e-mail,
// 6xxx privacidade (apagar conta e excluídos).
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

    // Confirmação de e-mail (docs/email.md, 5.1).
    [LoggerMessage(2006, LogLevel.Information, "User {UserId} confirmed their email")]
    public static partial void EmailConfirmed(this ILogger logger, Guid userId);

    [LoggerMessage(2007, LogLevel.Warning, "Email confirmation rejected: {Reason}")]
    public static partial void EmailConfirmationRejected(this ILogger logger, string reason);

    // Cadastro com e-mail que já tem conta: a resposta é a de sempre, e o dono do e-mail recebe um aviso.
    [LoggerMessage(2008, LogLevel.Information, "Registration with the address of user {UserId} (confirmed: {Confirmed})")]
    public static partial void RegistrationOfExistingAccount(this ILogger logger, Guid userId, bool confirmed);

    // Senha nova pelo e-mail (docs/email.md, 5.2).
    [LoggerMessage(2009, LogLevel.Information, "User {UserId} asked for a password reset link")]
    public static partial void PasswordResetRequested(this ILogger logger, Guid userId);

    [LoggerMessage(2010, LogLevel.Warning, "Password reset asked for an address with no account")]
    public static partial void PasswordResetRequestedForUnknownEmail(this ILogger logger);

    [LoggerMessage(2011, LogLevel.Information, "User {UserId} reset their password")]
    public static partial void PasswordReset(this ILogger logger, Guid userId);

    [LoggerMessage(2012, LogLevel.Warning, "Password reset rejected: {Reason}")]
    public static partial void PasswordResetRejected(this ILogger logger, string reason);

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

    [LoggerMessage(4000, LogLevel.Information, "Asset list fetched from {Provider}: {Assets} assets in {Pages} pages, {Skipped} skipped, in {ElapsedMs} ms")]
    public static partial void AssetListFetched(this ILogger logger, string provider, int assets, int pages, int skipped, double elapsedMs);

    [LoggerMessage(4001, LogLevel.Warning, "Asset list item from {Provider} skipped: {Symbol} ({Reason})")]
    public static partial void AssetListItemSkipped(this ILogger logger, string provider, string? symbol, string reason);

    // Tipo novo do fornecedor: os ativos entram como Unknown até a tradução aprender o tipo.
    [LoggerMessage(4002, LogLevel.Warning, "Asset list from {Provider} has an unknown kind: assetType {ProviderAssetType}, subType {ProviderSubType} ({Count} assets)")]
    public static partial void AssetKindUnknown(this ILogger logger, string provider, string? providerAssetType, string? providerSubType, int count);

    [LoggerMessage(4003, LogLevel.Information, "Asset catalog synced with {Listed} listed: {Added} added, {Updated} updated, {Reactivated} reactivated, {Deactivated} deactivated")]
    public static partial void AssetListSynced(this ILogger logger, int listed, int added, int updated, int reactivated, int deactivated);

    // Trava de sanidade: a lista veio muito menor que o catálogo e nada foi gravado.
    [LoggerMessage(4004, LogLevel.Warning, "Asset catalog sync refused: {Listed} listed against {ActiveBefore} active; nothing was written")]
    public static partial void AssetListSyncRefused(this ILogger logger, int activeBefore, int listed);

    [LoggerMessage(4005, LogLevel.Warning, "Asset catalog sync skipped: the market data provider is unavailable; the next run tries again")]
    public static partial void AssetListSyncUnavailable(this ILogger logger, Exception exception);

    [LoggerMessage(4006, LogLevel.Error, "Asset catalog sync failed; the next run tries again")]
    public static partial void AssetListSyncFailed(this ILogger logger, Exception exception);

    [LoggerMessage(4007, LogLevel.Information, "Next asset catalog sync at {NextRunUtc:o}")]
    public static partial void AssetListNextRun(this ILogger logger, DateTime nextRunUtc);

    [LoggerMessage(4008, LogLevel.Information, "Asset logos synced: {Pending} pending, {Saved} saved, {Removed} removed, {Failed} failed, {Rejected} rejected")]
    public static partial void AssetLogosSynced(this ILogger logger, int pending, int saved, int removed, int failed, int rejected);

    [LoggerMessage(4009, LogLevel.Warning, "Asset logo skipped for {Symbol}: {Reason}; the next run tries again")]
    public static partial void AssetLogoSkipped(this ILogger logger, string symbol, string reason);

    [LoggerMessage(4010, LogLevel.Error, "Asset logo sync failed; the next run tries again")]
    public static partial void AssetLogoSyncFailed(this ILogger logger, Exception exception);

    [LoggerMessage(4011, LogLevel.Information,
        "Corporate events synced: {AssetsAdded} historical assets added, {Added} events added, {Updated} updated, {Removed} removed")]
    public static partial void CorporateEventsSynced(this ILogger logger, int assetsAdded, int added, int updated, int removed);

    [LoggerMessage(4012, LogLevel.Warning, "Corporate event {Key} skipped: asset {Symbol} is not in the catalog")]
    public static partial void CorporateEventSkipped(this ILogger logger, string key, string symbol);

    [LoggerMessage(4013, LogLevel.Error, "Corporate events file refused, nothing changed: {Reason}")]
    public static partial void CorporateEventsRefused(this ILogger logger, string reason);

    [LoggerMessage(4014, LogLevel.Error, "Corporate events sync failed; the next start or catalog sync tries again")]
    public static partial void CorporateEventSyncFailed(this ILogger logger, Exception exception);

    [LoggerMessage(4100, LogLevel.Information, "Holding {HoldingId} of asset {AssetId} added to the portfolio")]
    public static partial void HoldingAdded(this ILogger logger, Guid holdingId, Guid assetId);

    [LoggerMessage(4101, LogLevel.Information, "Holding {HoldingId} removed from the portfolio")]
    public static partial void HoldingRemoved(this ILogger logger, Guid holdingId);

    [LoggerMessage(4102, LogLevel.Information, "Payout {TransactionId} of asset {AssetId} recorded ({Kind})")]
    public static partial void PayoutCreated(this ILogger logger, Guid transactionId, Guid assetId, Prisma.Domain.Investments.PayoutKind kind);

    [LoggerMessage(4103, LogLevel.Information, "Payout {TransactionId} edited")]
    public static partial void PayoutEdited(this ILogger logger, Guid transactionId);

    [LoggerMessage(4104, LogLevel.Information, "Trade note {NoteId} recorded with {TradeCount} trades")]
    public static partial void TradeNoteCreated(this ILogger logger, Guid noteId, int tradeCount);

    [LoggerMessage(4105, LogLevel.Information, "Trade note {NoteId} edited")]
    public static partial void TradeNoteEdited(this ILogger logger, Guid noteId);

    [LoggerMessage(4106, LogLevel.Information, "Trade note {NoteId} deleted")]
    public static partial void TradeNoteDeleted(this ILogger logger, Guid noteId);

    [LoggerMessage(4107, LogLevel.Information, "Trade note {NoteId} restored")]
    public static partial void TradeNoteRestored(this ILogger logger, Guid noteId);

    // O código não é dado pessoal; quem pediu vem no escopo (userId). A curadoria filtra por este evento (docs/operacao.md, 4.3).
    [LoggerMessage(4108, LogLevel.Information, "Asset {Symbol} requested ({RequestId})")]
    public static partial void AssetRequested(this ILogger logger, Guid requestId, string symbol);

    [LoggerMessage(4109, LogLevel.Information, "Asset request {RequestId} removed")]
    public static partial void AssetRequestRemoved(this ILogger logger, Guid requestId);

    [LoggerMessage(4110, LogLevel.Information, "Asset request {RequestId} restored")]
    public static partial void AssetRequestRestored(this ILogger logger, Guid requestId);

    // Conferência dos eventos (docs/investimentos.md, I.7c2): quantos, nunca a quantidade de ninguém. O "Não confere" é o
    // que a curadoria procura (docs/operacao.md, 4.4).
    [LoggerMessage(4111, LogLevel.Information, "{Count} corporate events confirmed")]
    public static partial void EventReviewsConfirmed(this ILogger logger, int count);

    [LoggerMessage(4112, LogLevel.Information, "Corporate event {EventId} disputed")]
    public static partial void EventReviewDisputed(this ILogger logger, Guid eventId);

    [LoggerMessage(4113, LogLevel.Information, "Review of corporate event {EventId} undone")]
    public static partial void EventReviewUndone(this ILogger logger, Guid eventId);

    // Fila de e-mails (docs/email.md, seção 3): o id da linha, o tipo e o motivo, nunca o endereço, o assunto, o link ou o
    // token. Para achar um e-mail que não chegou: o id leva à linha da fila (docs/operacao.md, 8.4).
    [LoggerMessage(5000, LogLevel.Information, "Email {OutboxId} ({Kind}) to user {UserId} sent after {Attempts} attempts")]
    public static partial void EmailSent(this ILogger logger, Guid outboxId, EmailKind kind, Guid userId, int attempts);

    [LoggerMessage(5001, LogLevel.Warning, "Email {OutboxId} ({Kind}) not sent ({Reason}); next attempt at {NextAttemptAt}")]
    public static partial void EmailRetryScheduled(this ILogger logger, Guid outboxId, EmailKind kind, string? reason, DateTime nextAttemptAt);

    [LoggerMessage(5002, LogLevel.Error, "Email {OutboxId} ({Kind}) gave up after {Attempts} attempts ({Reason})")]
    public static partial void EmailFailed(this ILogger logger, Guid outboxId, EmailKind kind, int attempts, string? reason);

    [LoggerMessage(5003, LogLevel.Information, "Email {OutboxId} ({Kind}) skipped: no longer needed")]
    public static partial void EmailSkipped(this ILogger logger, Guid outboxId, EmailKind kind);

    [LoggerMessage(5004, LogLevel.Warning, "Email {Kind} for user {UserId} not queued: limit reached")]
    public static partial void EmailThrottled(this ILogger logger, Guid userId, EmailKind kind);

    [LoggerMessage(5005, LogLevel.Information, "Email outbox worker started: checks every {Seconds} s")]
    public static partial void EmailOutboxWorkerStarted(this ILogger logger, int seconds);

    [LoggerMessage(5006, LogLevel.Error, "Email outbox run failed; the next run tries again")]
    public static partial void EmailOutboxRunFailed(this ILogger logger, Exception exception);

    [LoggerMessage(5007, LogLevel.Error, "Email {OutboxId} could not be prepared; it is tried again later")]
    public static partial void EmailProcessingFailed(this ILogger logger, Guid outboxId, Exception exception);

    [LoggerMessage(5008, LogLevel.Information, "Removed {Count} old emails from the outbox")]
    public static partial void EmailOutboxCleaned(this ILogger logger, int count);

    // Apagar a conta e o excluído (etapa A.3). O AccountDeleted guarda o id fora do banco: se o banco se perder, é por
    // ele que se acham as contas a apagar de novo depois de restaurar um backup (docs/operacao.md, 5.6).
    [LoggerMessage(6000, LogLevel.Information, "User {UserId} deleted their account")]
    public static partial void AccountDeleted(this ILogger logger, Guid userId);

    [LoggerMessage(6001, LogLevel.Warning, "Account deletion rejected for user {UserId}: {Reason}")]
    public static partial void AccountDeletionRejected(this ILogger logger, Guid userId, string reason);

    [LoggerMessage(6002, LogLevel.Information, "Purged {Count} deleted rows")]
    public static partial void DeletedItemsPurged(this ILogger logger, int count);

    [LoggerMessage(6003, LogLevel.Warning, "Deleted account {UserId} was back (restored backup?) and was erased again")]
    public static partial void AccountDeletionReapplied(this ILogger logger, Guid userId);

    [LoggerMessage(6004, LogLevel.Information, "Erasure worker started, running every {IntervalMinutes} minutes")]
    public static partial void ErasureWorkerStarted(this ILogger logger, double intervalMinutes);

    [LoggerMessage(6005, LogLevel.Error, "Erasure run failed")]
    public static partial void ErasureRunFailed(this ILogger logger, Exception exception);

    [LoggerMessage(6006, LogLevel.Information, "Forgot {Count} account deletions older than the backup retention")]
    public static partial void DeletedAccountsForgotten(this ILogger logger, int count);

    // Aceite dos termos por quem já tinha conta (etapa A.4); o do cadastro vai junto com o UserRegistered.
    [LoggerMessage(6007, LogLevel.Information, "User {UserId} accepted terms {TermsVersion} and privacy policy {PrivacyVersion}")]
    public static partial void TermsAccepted(this ILogger logger, Guid userId, string termsVersion, string privacyVersion);

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
