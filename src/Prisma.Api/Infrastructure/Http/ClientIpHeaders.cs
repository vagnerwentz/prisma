using System.Net;
using Microsoft.Extensions.Primitives;
using Prisma.Api.Infrastructure.Logging;

namespace Prisma.Api.Infrastructure.Http;

// O retrato dos cabeçalhos de IP que o proxy do Railway manda (etapa S.1a, diagnóstico). A API lia o último valor do
// X-Forwarded-For, que é um salto interno do Railway: o rate limit por IP juntava todo mundo. A documentação do Railway
// cita só o X-Real-IP para o IP do cliente; o fórum diverge sobre os dois. Antes de trocar, cada pedido à API registra
// o formato do que chegou, sem IP nenhum: se o X-Real-IP vem, se é interno, se bate com o começo ou o fim da lista do
// X-Forwarded-For. Um endereço interno forjado pelo cliente aparece como "interno" no retrato.
//
// O X-Railway-Request-Id liga a linha ao log de rede do Railway, que mostra o IP que a borda viu.
public sealed record ClientIpReport(
    bool RealIpPresent,
    bool? RealIpInternal,
    int ForwardedCount,
    bool? ForwardedFirstInternal,
    bool? ForwardedLastInternal,
    bool RealIpEqualsForwardedFirst,
    bool RealIpEqualsForwardedLast,
    string? Edge,
    string? RailwayRequestId);

public static class ClientIpHeaders
{
    public const int MaxIdLength = 100;

    public static ClientIpReport Read(IHeaderDictionary headers)
    {
        var realIp = First(headers["X-Real-IP"]);
        var forwarded = headers["X-Forwarded-For"]
            .SelectMany(line => (line ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();

        var real = Parse(realIp);
        var first = forwarded.Count > 0 ? Parse(forwarded[0]) : null;
        var last = forwarded.Count > 0 ? Parse(forwarded[^1]) : null;

        return new ClientIpReport(
            RealIpPresent: realIp is not null,
            RealIpInternal: InternalOrUnknown(realIp, real),
            ForwardedCount: forwarded.Count,
            ForwardedFirstInternal: forwarded.Count > 0 ? InternalOrUnknown(forwarded[0], first) : null,
            ForwardedLastInternal: forwarded.Count > 0 ? InternalOrUnknown(forwarded[^1], last) : null,
            RealIpEqualsForwardedFirst: real is not null && real.Equals(first),
            RealIpEqualsForwardedLast: real is not null && real.Equals(last),
            Edge: Short(First(headers["X-Railway-Edge"])),
            RailwayRequestId: Short(First(headers["X-Railway-Request-Id"])));
    }

    // Só atrás do proxy (produção) e só nos pedidos à API. Antes do UseForwardedHeaders, que tira os cabeçalhos que usa.
    public static void UseClientIpDiagnostics(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
            return;

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Prisma.Api.ClientIp");
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(FrontendHosting.ApiPrefix))
                logger.ClientIpHeaders(Read(context.Request.Headers));
            await next(context);
        });
    }

    private static string? First(StringValues values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    // Aceita "1.2.3.4", "1.2.3.4:5678", "2001:db8::1" e "[2001:db8::1]:443".
    private static IPAddress? Parse(string? text)
    {
        if (text is null)
            return null;
        if (IPAddress.TryParse(text, out var address))
            return Normalize(address);
        return IPEndPoint.TryParse(text, out var endpoint) ? Normalize(endpoint.Address) : null;
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    // Texto que não é endereço fica sem resposta (null), em vez de passar por externo.
    private static bool? InternalOrUnknown(string? text, IPAddress? address) =>
        text is null || address is null ? null : ClientAddress.IsInternal(address);

    private static string? Short(string? value) =>
        value is null ? null : value.Length > MaxIdLength ? value[..MaxIdLength] : value;
}
