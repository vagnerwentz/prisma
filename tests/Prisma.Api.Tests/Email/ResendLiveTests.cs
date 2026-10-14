using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Email.Resend;
using Prisma.Api.Tests.Market;
using Shouldly;

namespace Prisma.Api.Tests.Email;

// Contrato com o Resend de verdade: o pedido que o cliente monta ainda é aceito. Fora do `dotnet test` e do CI. Envia
// para o endereço de teste do Resend (delivered@resend.dev), que não entrega a ninguém, mas gasta um da cota:
//
//   PRISMA_LIVE_TESTS=1 PRISMA_RESEND_API_KEY=<chave> dotnet test tests/Prisma.Api.Tests --filter Category=Live
//
// Sem o domínio verificado, troque o remetente por onboarding@resend.dev em PRISMA_RESEND_FROM.
[Trait("Category", "Live")]
public sealed class ResendLiveTests
{
    [LiveFact]
    public async Task The_real_api_accepts_the_message()
    {
        var key = Environment.GetEnvironmentVariable("PRISMA_RESEND_API_KEY");
        key.ShouldNotBeNullOrWhiteSpace("Defina PRISMA_RESEND_API_KEY com uma chave de envio do Resend.");
        var from = Environment.GetEnvironmentVariable("PRISMA_RESEND_FROM") ?? "Prisma Money <nao-responda@prismamoney.com.br>";

        var options = new ResendOptions();
        using var http = new HttpClient { BaseAddress = options.BaseUrl, Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Prisma/1.0");
        http.DefaultRequestHeaders.Authorization = new("Bearer", key);

        var result = await new ResendEmailTransport(http).SendAsync(new EmailMessage(
            from,
            "contato@prismamoney.com.br",
            "delivered@resend.dev",
            "Teste de contrato do Prisma Money",
            "<p>Teste de contrato.</p>",
            "Teste de contrato."), CancellationToken.None);

        result.Status.ShouldBe(EmailSendStatus.Sent, result.Reason);
        result.ProviderMessageId.ShouldNotBeNullOrWhiteSpace();
    }
}
