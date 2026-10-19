using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Api.Infrastructure.Privacy;
using Prisma.Domain;

namespace Prisma.Api.Features.Auth;

// O aceite de quem já tem conta (etapa A.4): quem se cadastrou antes do aceite existir, ou aceitou uma versão anterior
// dos termos ou da política, vê a tela de aceite ao entrar (SessionUser.MustAcceptTerms). O botão só se acende com a
// caixa marcada; a API grava as versões vigentes e o instante (LegalTerms).
public static class AcceptTerms
{
    public sealed class Handler(UserManager<AppUser> users, ICurrentUser currentUser, IClock clock, IOptions<AdminOptions> admins, ILogger<Handler> logger)
    {
        public async Task<Result<SessionUser>> Execute(CancellationToken ct)
        {
            var user = await users.FindByIdAsync(currentUser.UserId.ToString());
            if (user is null)
                return new Error(ErrorType.Unauthorized, "Sessão expirada. Entre de novo.");

            LegalTerms.Accept(user, clock.UtcNow);
            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded)
                throw new InvalidOperationException("Não foi possível gravar o aceite: " + string.Join(",", updated.Errors.Select(e => e.Code)));

            logger.TermsAccepted(user.Id, LegalTerms.TermsVersion, LegalTerms.PrivacyVersion);
            return SessionUser.Of(user, admins.Value);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/accept-terms", async (Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<SessionUser>(200)
            .ProducesProblem(401);
}
