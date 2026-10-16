using System.Net;
using Microsoft.Extensions.Primitives;
using Prisma.Api.Infrastructure.Logging;

namespace Prisma.Api.Infrastructure.Http;

// O retrato dos cabeçalhos de IP que o proxy do Railway manda (etapa S.1). A API lia o último valor do X-Forwarded-For,
// um IP da borda do Railway igual para todo mundo: o rate limit por IP juntava os usuários (M4). O diagnóstico da S.1a
// mediu na produção (2026-10-15): o X-Real-IP vem, não é interno, é igual ao primeiro valor do X-Forwarded-For, e o
// Railway descarta os dois quando o cliente os forja. Desde a S.1b a API lê o X-Real-IP (Program.cs), o cabeçalho que a
// documentação do Railway cita para o IP do cliente.
//
// Se o formato mudar, o alerta ClientIpHeadersUnexpected sai a cada pedido, com o retrato e sem IP nenhum: um endereço
// interno aparece como "interno". O X-Railway-Request-Id liga a linha ao log de rede do Railway.
public sealed record ClientIpReport(
    bool RealIpPresent,
    bool? RealIpInternal,
    int ForwardedCount,
    bool? ForwardedFirstInternal,
    bool? ForwardedLastInternal,
    bool RealIpEqualsForwardedFirst,
    bool RealIpEqualsForwardedLast,
    string? Edge,
    string? RailwayRequestId)
{
    // Pedido sem nenhum cabeçalho da borda (uma conferência interna, por exemplo) não tem o que conferir.
    public bool CameThroughTheEdge => ForwardedCount > 0 || RailwayRequestId is not null;

    // O que a S.1a mediu é o esperado. Sem o X-Real-IP, a API cairia no IP do proxy; interno, ela leria um salto do
    // Railway; diferente do começo da lista, o Railway mudou o formato (ou o X-Real-IP traz a CDN, defeito citado no fórum).
    public bool LooksWrong =>
        CameThroughTheEdge && (!RealIpPresent || RealIpInternal != false || !RealIpEqualsForwardedFirst);
}

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

    // Só atrás do proxy (produção) e só nos pedidos à API. Antes do UseForwardedHeaders, que tira o cabeçalho que usa.
    public static void UseClientIpCheck(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
            return;

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Prisma.Api.ClientIp");
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(FrontendHosting.ApiPrefix)
                && Read(context.Request.Headers) is { LooksWrong: true } report)
                logger.ClientIpHeadersUnexpected(report);
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
