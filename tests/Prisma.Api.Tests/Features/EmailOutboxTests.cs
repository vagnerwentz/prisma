using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Tests.Email;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/email.md, seção 3 (etapa H.4a): a fila de e-mails no Postgres. O pedido nasce com a gravação de quem pede, a
// tarefa envia, a falha passageira espera e tenta de novo, a definitiva desiste, e nada sai duas vezes.
[Collection(ApiCollection.Name)]
public sealed class EmailOutboxTests(PostgresFixture postgres)
{
    // Monta o conteúdo no lugar dos tipos de verdade (que entram com cada fluxo, a partir da H.4b). Responde pelo
    // ConfirmEmail; o ResetPassword fica sem quem o monte.
    private sealed class TestComposer : IEmailComposer
    {
        public ConcurrentDictionary<Guid, bool> Skip { get; } = new();

        public ConcurrentDictionary<Guid, bool> Throw { get; } = new();

        public EmailKind Kind => EmailKind.ConfirmEmail;

        public Task<EmailContent?> ComposeAsync(OutboxEmail email, CancellationToken ct)
        {
            if (Throw.ContainsKey(email.UserId))
                throw new InvalidOperationException("banco fora do ar");
            return Task.FromResult(Skip.ContainsKey(email.UserId)
                ? null
                : new EmailContent($"Confirme seu e-mail ({email.Id})", "<p>Confirme</p>", "Confirme"));
        }
    }

    private static readonly DateTime Start = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeClock _clock = new(Start);
    private readonly TestComposer _composer = new();
    private readonly LogSink _logs = new();

    // Lote grande: a fila é do banco compartilhado, e e-mails de outros testes não podem tomar o lugar dos deste.
    private PrismaApiFactory Factory() => new(postgres.ConnectionString, new Dictionary<string, string>
    {
        ["RateLimiting:Auth:PermitLimit"] = "1000",
        ["Email:Outbox:BatchSize"] = "500",
    }, _clock, _logs, services => services.AddSingleton<IEmailComposer>(_composer));

    private static async Task<(Guid Id, string Address)> NewUser(PrismaApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var address = $"{Guid.NewGuid():N}@teste.com.br";
        var user = new AppUser { UserName = address, Email = address };
        (await users.CreateAsync(user)).Succeeded.ShouldBeTrue();
        return (user.Id, address);
    }

    private static async Task<bool> Enqueue(PrismaApiFactory factory, Guid userId, string address,
        EmailKind kind = EmailKind.ConfirmEmail)
    {
        using var scope = factory.Services.CreateScope();
        var queued = await scope.ServiceProvider.GetRequiredService<EmailQueue>()
            .EnqueueAsync(kind, userId, address, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
        return queued;
    }

    private static async Task<List<OutboxEmail>> RowsOf(PrismaApiFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxEmails
            .AsNoTracking().Where(e => e.UserId == userId).OrderBy(e => e.CreatedAt).ToListAsync();
    }

    private static Task<EmailRunSummary> Run(PrismaApiFactory factory) =>
        factory.Services.GetRequiredService<EmailOutboxProcessor>().RunOnceAsync(CancellationToken.None);

    private static FakeEmailTransport Transport(PrismaApiFactory factory) =>
        factory.Services.GetRequiredService<FakeEmailTransport>();

    [Fact]
    public async Task A_queued_email_is_sent_by_the_next_run()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);

        (await Enqueue(factory, userId, address)).ShouldBeTrue();
        var row = (await RowsOf(factory, userId)).ShouldHaveSingleItem();
        row.Status.ShouldBe(EmailStatus.Pending);
        Transport(factory).To(address).ShouldBeEmpty();

        await Run(factory);

        var message = Transport(factory).To(address).ShouldHaveSingleItem();
        message.From.ShouldBe("Prisma Money <nao-responda@prismamoney.com.br>");
        message.ReplyTo.ShouldBe("contato@prismamoney.com.br");
        message.Subject.ShouldBe($"Confirme seu e-mail ({row.Id})");
        message.Text.ShouldBe("Confirme");

        var sent = (await RowsOf(factory, userId)).ShouldHaveSingleItem();
        sent.Status.ShouldBe(EmailStatus.Sent);
        sent.Attempts.ShouldBe(1);
        sent.SentAt.ShouldBe(Start);
        sent.ProviderMessageId.ShouldStartWith("fake-");
        sent.LastError.ShouldBeNull();

        var log = _logs.Entries.Single(e => e.EventId.Name == "EmailSent" && Equals(e["OutboxId"], row.Id));
        log["UserId"].ShouldBe(userId);
        log["Kind"].ShouldBe(EmailKind.ConfirmEmail);

        // Já enviado: a próxima volta não manda de novo.
        _clock.UtcNow = Start.AddDays(1);
        await Run(factory);
        Transport(factory).To(address).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Nothing_is_queued_when_the_reason_is_not_saved()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<EmailQueue>()
                .EnqueueAsync(EmailKind.ConfirmEmail, userId, address, CancellationToken.None);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            // Pedido sem SaveChanges também não existe.
            await scope.ServiceProvider.GetRequiredService<EmailQueue>()
                .EnqueueAsync(EmailKind.ConfirmEmail, userId, address, CancellationToken.None);
        }

        await Run(factory);

        (await RowsOf(factory, userId)).ShouldBeEmpty();
        Transport(factory).To(address).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_transient_failure_waits_and_tries_again()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);
        var transport = Transport(factory);
        transport.Respond = m => Task.FromResult(m.To == address && transport.To(address).Count == 1
            ? EmailSendResult.Retry("http_503_service_unavailable")
            : EmailSendResult.Sent("re_1"));
        await Enqueue(factory, userId, address);

        await Run(factory);

        var waiting = (await RowsOf(factory, userId)).ShouldHaveSingleItem();
        waiting.Status.ShouldBe(EmailStatus.Pending);
        waiting.Attempts.ShouldBe(1);
        waiting.LastError.ShouldBe("http_503_service_unavailable");
        waiting.NextAttemptAt.ShouldBe(Start.AddSeconds(30));
        var warning = _logs.Entries.Single(e => e.EventId.Name == "EmailRetryScheduled" && Equals(e["OutboxId"], waiting.Id));
        warning.Level.ShouldBe(LogLevel.Warning);
        warning["Reason"].ShouldBe("http_503_service_unavailable");

        // Antes da hora, fica onde está.
        _clock.UtcNow = Start.AddSeconds(29);
        await Run(factory);
        transport.To(address).Count.ShouldBe(1);

        _clock.UtcNow = Start.AddSeconds(30);
        await Run(factory);

        transport.To(address).Count.ShouldBe(2);
        var sent = (await RowsOf(factory, userId)).ShouldHaveSingleItem();
        sent.Status.ShouldBe(EmailStatus.Sent);
        sent.Attempts.ShouldBe(2);
        sent.SentAt.ShouldBe(Start.AddSeconds(30));
        sent.LastError.ShouldBeNull();
    }

    [Fact]
    public async Task A_rejected_email_fails_at_once()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);
        Transport(factory).Respond = m => Task.FromResult(m.To == address
            ? EmailSendResult.Reject("http_422_validation_error")
            : EmailSendResult.Sent("re_1"));
        await Enqueue(factory, userId, address);

        await Run(factory);
        _clock.UtcNow = Start.AddHours(2);
        await Run(factory);

        Transport(factory).To(address).ShouldHaveSingleItem();
        var failed = (await RowsOf(factory, userId)).ShouldHaveSingleItem();
        failed.Status.ShouldBe(EmailStatus.Failed);
        failed.Attempts.ShouldBe(1);
        failed.LastError.ShouldBe("http_422_validation_error");
        var error = _logs.Entries.Single(e => e.EventId.Name == "EmailFailed" && Equals(e["OutboxId"], failed.Id));
        error.Level.ShouldBe(LogLevel.Error);
    }

    // 30 s, 2 min, 10 min, 1 h e depois 6 h: tentativas em 0, 30 s, 2,5 min, 12,5 min, 1h12,5, 7h12,5, 13h12,5 e
    // 19h12,5. A seguinte cairia em 25h12,5, depois das 24 h: desiste na oitava.
    [Fact]
    public async Task Gives_up_after_24_hours_of_failures()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);
        Transport(factory).Respond = m => Task.FromResult(m.To == address
            ? EmailSendResult.Retry("timeout")
            : EmailSendResult.Sent("re_1"));
        await Enqueue(factory, userId, address);

        var attemptTimes = new List<DateTime>();
        for (var turn = 0; turn < 20; turn++)
        {
            var row = (await RowsOf(factory, userId)).Single();
            if (row.Status != EmailStatus.Pending)
                break;
            _clock.UtcNow = row.NextAttemptAt;
            attemptTimes.Add(_clock.UtcNow);
            await Run(factory);
        }

        var failed = (await RowsOf(factory, userId)).ShouldHaveSingleItem();
        failed.Status.ShouldBe(EmailStatus.Failed);
        failed.Attempts.ShouldBe(8);
        failed.LastError.ShouldBe("timeout");
        Transport(factory).To(address).Count.ShouldBe(8);
        attemptTimes.ShouldBe([
            Start, Start.AddSeconds(30), Start.AddMinutes(2.5), Start.AddMinutes(12.5), Start.AddMinutes(72.5),
            Start.AddMinutes(432.5), Start.AddMinutes(792.5), Start.AddMinutes(1152.5),
        ]);
        _logs.Entries.ShouldContain(e => e.EventId.Name == "EmailFailed" && Equals(e["OutboxId"], failed.Id));
    }

    [Fact]
    public async Task An_email_no_longer_needed_is_skipped()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);
        _composer.Skip[userId] = true;
        await Enqueue(factory, userId, address);

        await Run(factory);

        Transport(factory).To(address).ShouldBeEmpty();
        (await RowsOf(factory, userId)).ShouldHaveSingleItem().Status.ShouldBe(EmailStatus.Skipped);
    }

    [Fact]
    public async Task An_error_while_composing_is_retried_like_a_transient_failure()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);
        _composer.Throw[userId] = true;
        await Enqueue(factory, userId, address);

        await Run(factory);

        var row = (await RowsOf(factory, userId)).ShouldHaveSingleItem();
        row.Status.ShouldBe(EmailStatus.Pending);
        row.LastError.ShouldBe("exception");
        row.NextAttemptAt.ShouldBe(Start.AddSeconds(30));
        _logs.Entries.Single(e => e.EventId.Name == "EmailProcessingFailed" && Equals(e["OutboxId"], row.Id))
            .Exception.ShouldBeOfType<InvalidOperationException>();

        _composer.Throw.TryRemove(userId, out _);
        _clock.UtcNow = Start.AddSeconds(30);
        await Run(factory);

        (await RowsOf(factory, userId)).ShouldHaveSingleItem().Status.ShouldBe(EmailStatus.Sent);
    }

    [Fact]
    public async Task A_kind_without_composer_fails()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);
        await Enqueue(factory, userId, address, EmailKind.ResetPassword);

        await Run(factory);

        Transport(factory).To(address).ShouldBeEmpty();
        var row = (await RowsOf(factory, userId)).ShouldHaveSingleItem();
        row.Status.ShouldBe(EmailStatus.Failed);
        row.LastError.ShouldBe("no_composer");
    }

    [Fact]
    public async Task Two_runs_at_the_same_time_send_each_email_once()
    {
        await using var factory = Factory();
        var recipients = new List<(Guid Id, string Address)>();
        for (var i = 0; i < 10; i++)
        {
            var user = await NewUser(factory);
            recipients.Add(user);
            await Enqueue(factory, user.Id, user.Address);
        }

        var transport = Transport(factory);
        transport.Respond = async _ =>
        {
            await Task.Delay(50);
            return EmailSendResult.Sent("re_1");
        };

        await Task.WhenAll(Run(factory), Run(factory), Run(factory));

        foreach (var (id, address) in recipients)
        {
            transport.To(address).ShouldHaveSingleItem();
            (await RowsOf(factory, id)).ShouldHaveSingleItem().Status.ShouldBe(EmailStatus.Sent);
        }
    }

    // Outra execução está reservando a mesma linha neste instante (trava da linha aberta): esta pula a linha em vez de
    // esperar, e não a pega duas vezes. Sem o SKIP LOCKED, ficaria parada até a outra terminar.
    [Fact]
    public async Task A_row_being_claimed_elsewhere_is_skipped_not_waited_on()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);
        await Enqueue(factory, userId, address);
        var rowId = (await RowsOf(factory, userId)).Single().Id;

        await using (var other = new Npgsql.NpgsqlConnection(postgres.ConnectionString))
        {
            await other.OpenAsync();
            await using var transaction = await other.BeginTransactionAsync();
            await using (var lockRow = new Npgsql.NpgsqlCommand("SELECT id FROM outbox_emails WHERE id = @id FOR UPDATE", other, transaction))
            {
                lockRow.Parameters.AddWithValue("id", rowId);
                await lockRow.ExecuteScalarAsync();
            }

            var run = Run(factory);
            (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)))).ShouldBe(run, "A execução esperou a trava.");
            Transport(factory).To(address).ShouldBeEmpty();
            await transaction.RollbackAsync();
        }

        await Run(factory);

        Transport(factory).To(address).ShouldHaveSingleItem();
    }

    // A API para no meio do envio: o e-mail fica reservado por 2 min e volta para a fila depois disso.
    [Fact]
    public async Task An_email_interrupted_mid_send_returns_after_the_lease()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);
        await Enqueue(factory, userId, address);
        using var stopping = new CancellationTokenSource();
        var transport = Transport(factory);
        transport.Respond = async m =>
        {
            if (m.To != address)
                return EmailSendResult.Sent("re_1");
            await stopping.CancelAsync();
            stopping.Token.ThrowIfCancellationRequested();
            return EmailSendResult.Sent("re_1");
        };

        await Should.ThrowAsync<OperationCanceledException>(() =>
            factory.Services.GetRequiredService<EmailOutboxProcessor>().RunOnceAsync(stopping.Token));

        var reserved = (await RowsOf(factory, userId)).ShouldHaveSingleItem();
        reserved.Status.ShouldBe(EmailStatus.Pending);
        reserved.Attempts.ShouldBe(0);
        reserved.NextAttemptAt.ShouldBe(Start + EmailOutboxProcessor.Lease);

        transport.Respond = _ => Task.FromResult(EmailSendResult.Sent("re_2"));
        _clock.UtcNow = Start.AddMinutes(1);
        await Run(factory);
        transport.To(address).Count.ShouldBe(1);

        _clock.UtcNow = Start + EmailOutboxProcessor.Lease;
        await Run(factory);

        transport.To(address).Count.ShouldBe(2);
        (await RowsOf(factory, userId)).ShouldHaveSingleItem().Status.ShouldBe(EmailStatus.Sent);
    }

    [Fact]
    public async Task One_email_of_a_kind_per_person_per_minute()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);
        var (otherId, otherAddress) = await NewUser(factory);

        (await Enqueue(factory, userId, address)).ShouldBeTrue();
        _clock.UtcNow = Start.AddSeconds(59);
        (await Enqueue(factory, userId, address)).ShouldBeFalse();
        // Outro tipo e outra pessoa não contam.
        (await Enqueue(factory, userId, address, EmailKind.PasswordChanged)).ShouldBeTrue();
        (await Enqueue(factory, otherId, otherAddress)).ShouldBeTrue();

        _clock.UtcNow = Start.AddMinutes(1).AddSeconds(1);
        (await Enqueue(factory, userId, address)).ShouldBeTrue();

        (await RowsOf(factory, userId)).Count(e => e.Kind == EmailKind.ConfirmEmail).ShouldBe(2);
        var throttled = _logs.Entries.Single(e => e.EventId.Name == "EmailThrottled" && Equals(e["UserId"], userId));
        throttled["Kind"].ShouldBe(EmailKind.ConfirmEmail);
    }

    [Fact]
    public async Task At_most_five_emails_of_a_kind_per_person_per_day()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);

        for (var i = 0; i < 5; i++)
        {
            _clock.UtcNow = Start.AddMinutes(10 * i);
            (await Enqueue(factory, userId, address)).ShouldBeTrue();
        }

        _clock.UtcNow = Start.AddHours(23);
        (await Enqueue(factory, userId, address)).ShouldBeFalse();

        // 24 h depois do primeiro, ele deixa de contar.
        _clock.UtcNow = Start.AddDays(1).AddSeconds(1);
        (await Enqueue(factory, userId, address)).ShouldBeTrue();

        (await RowsOf(factory, userId)).Count.ShouldBe(6);
    }

    [Fact]
    public async Task Sent_and_failed_emails_are_removed_after_30_days()
    {
        await using var factory = Factory();
        var (oldId, oldAddress) = await NewUser(factory);
        var (failedId, failedAddress) = await NewUser(factory);
        var (recentId, recentAddress) = await NewUser(factory);
        var (pendingId, pendingAddress) = await NewUser(factory);
        Transport(factory).Respond = m => Task.FromResult(m.To == failedAddress
            ? EmailSendResult.Reject("http_422_validation_error")
            : EmailSendResult.Sent("re_1"));

        await Enqueue(factory, oldId, oldAddress);
        await Enqueue(factory, failedId, failedAddress);
        await Enqueue(factory, pendingId, pendingAddress);
        await Run(factory);
        _clock.UtcNow = Start.AddDays(2);
        await Enqueue(factory, recentId, recentAddress);
        await Run(factory);

        // Um pendente antigo (não deveria existir, mas a limpeza não pode apagar o que ainda vai sair).
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlAsync(
                $"UPDATE outbox_emails SET status = 'Pending', next_attempt_at = '2100-01-01' WHERE user_id = {pendingId}");

        _clock.UtcNow = Start.AddDays(31);
        var removed = await factory.Services.GetRequiredService<EmailOutboxProcessor>().CleanupAsync(CancellationToken.None);

        removed.ShouldBeGreaterThanOrEqualTo(2);
        (await RowsOf(factory, oldId)).ShouldBeEmpty();
        (await RowsOf(factory, failedId)).ShouldBeEmpty();
        (await RowsOf(factory, recentId)).ShouldHaveSingleItem();
        (await RowsOf(factory, pendingId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Logs_never_carry_the_address()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);
        Transport(factory).Respond = m => Task.FromResult(m.To == address
            ? EmailSendResult.Retry("http_503")
            : EmailSendResult.Sent("re_1"));
        await Enqueue(factory, userId, address);
        await Enqueue(factory, userId, address);

        await Run(factory);
        _clock.UtcNow = Start.AddDays(2);
        await Run(factory);

        _logs.Entries.Where(e => e.EventId.Name?.StartsWith("Email") == true).ShouldNotBeEmpty();
        _logs.Entries.ShouldAllBe(e => !e.Message.Contains(address) &&
                                       e.Properties.Values.All(v => v == null || !v.ToString()!.Contains(address)));
    }

    [Fact]
    public async Task Deleting_the_user_removes_their_queued_emails()
    {
        await using var factory = Factory();
        var (userId, address) = await NewUser(factory);
        Transport(factory).Respond = m => Task.FromResult(m.To == address
            ? EmailSendResult.Retry("http_503")
            : EmailSendResult.Sent("re_1"));
        await Enqueue(factory, userId, address);
        await Run(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            (await users.DeleteAsync((await users.FindByIdAsync(userId.ToString()))!)).Succeeded.ShouldBeTrue();
        }

        (await RowsOf(factory, userId)).ShouldBeEmpty();
    }
}
