using Microsoft.AspNetCore.Http;
using Prisma.Api.Infrastructure.Http;
using Shouldly;

namespace Prisma.Api.Tests.Http;

// Etapa S.1a (PLAN.md): o retrato dos cabeçalhos de IP que o proxy manda, sem IP nenhum, para decidir qual deles é o
// cliente de verdade.
public sealed class ClientIpHeadersTests
{
    private static ClientIpReport Read(params (string Name, string Value)[] headers)
    {
        var dictionary = new HeaderDictionary();
        foreach (var (name, value) in headers)
            dictionary.Append(name, value);
        return ClientIpHeaders.Read(dictionary);
    }

    [Fact]
    public void The_documented_railway_format_is_one_real_ip_equal_to_the_first_forwarded()
    {
        var report = Read(
            ("X-Real-IP", "203.0.113.10"),
            ("X-Forwarded-For", "203.0.113.10, 100.64.0.7"),
            ("X-Railway-Edge", "railway/us-west2"),
            ("X-Railway-Request-Id", "abc123"));

        report.ShouldBe(new ClientIpReport(
            RealIpPresent: true, RealIpInternal: false,
            ForwardedCount: 2, ForwardedFirstInternal: false, ForwardedLastInternal: true,
            RealIpEqualsForwardedFirst: true, RealIpEqualsForwardedLast: false,
            Edge: "railway/us-west2", RailwayRequestId: "abc123"));
    }

    // O que um cliente forjaria: um endereço interno na frente da lista. O retrato mostra que ele chegou ao app.
    [Fact]
    public void A_forged_internal_address_shows_up_as_internal()
    {
        var report = Read(("X-Real-IP", "10.0.0.5"), ("X-Forwarded-For", "10.0.0.5, 203.0.113.10, 100.64.0.7"));

        report.RealIpInternal.ShouldBe(true);
        report.ForwardedCount.ShouldBe(3);
        report.ForwardedFirstInternal.ShouldBe(true);
        report.RealIpEqualsForwardedFirst.ShouldBeTrue();
    }

    [Fact]
    public void Several_header_lines_count_as_one_list()
    {
        var report = Read(("X-Forwarded-For", "203.0.113.10"), ("X-Forwarded-For", "100.64.0.7"));

        report.ForwardedCount.ShouldBe(2);
        report.ForwardedFirstInternal.ShouldBe(false);
        report.ForwardedLastInternal.ShouldBe(true);
    }

    [Fact]
    public void Addresses_with_a_port_are_the_same_address()
    {
        var report = Read(("X-Real-IP", "203.0.113.10"), ("X-Forwarded-For", "203.0.113.10:51234, [2001:db8::1]:443"));

        report.RealIpEqualsForwardedFirst.ShouldBeTrue();
        report.ForwardedLastInternal.ShouldBe(false);
    }

    [Fact]
    public void Without_the_headers_nothing_is_present()
    {
        Read().ShouldBe(new ClientIpReport(
            RealIpPresent: false, RealIpInternal: null,
            ForwardedCount: 0, ForwardedFirstInternal: null, ForwardedLastInternal: null,
            RealIpEqualsForwardedFirst: false, RealIpEqualsForwardedLast: false,
            Edge: null, RailwayRequestId: null));
    }

    // Texto que não é endereço não vira "interno" nem "externo": fica sem resposta.
    [Fact]
    public void Text_that_is_not_an_address_is_unknown()
    {
        var report = Read(("X-Real-IP", "unknown"), ("X-Forwarded-For", "garbage"));

        report.RealIpPresent.ShouldBeTrue();
        report.RealIpInternal.ShouldBeNull();
        report.ForwardedFirstInternal.ShouldBeNull();
    }

    // Etapa S.1b: o formato medido na produção (2026-10-15) é o esperado; qualquer desvio vira alerta.
    [Fact]
    public void The_format_measured_in_production_is_expected()
    {
        Read(("X-Real-IP", "203.0.113.10"), ("X-Forwarded-For", "203.0.113.10, 198.51.100.7"), ("X-Railway-Edge", "gru1"))
            .LooksWrong.ShouldBeFalse();
    }

    [Theory]
    // Sem o X-Real-IP, a API cairia no IP do proxy.
    [InlineData(null, "203.0.113.10, 198.51.100.7")]
    // Interno: a API estaria lendo um salto do Railway.
    [InlineData("100.64.0.7", "100.64.0.7, 198.51.100.7")]
    // Diferente do começo da lista: o Railway mudou o formato (ou o X-Real-IP traz a CDN, o defeito citado no fórum).
    [InlineData("198.51.100.7", "203.0.113.10, 198.51.100.7")]
    [InlineData("garbage", "203.0.113.10, 198.51.100.7")]
    public void A_different_format_looks_wrong(string? realIp, string forwardedFor)
    {
        var headers = new List<(string, string)> { ("X-Forwarded-For", forwardedFor) };
        if (realIp is not null)
            headers.Add(("X-Real-IP", realIp));

        Read([.. headers]).LooksWrong.ShouldBeTrue();
    }

    // Pedido que não passou pela borda do Railway (nenhum cabeçalho dela): nada a conferir.
    [Fact]
    public void A_request_that_did_not_come_through_the_edge_is_not_checked()
    {
        Read().LooksWrong.ShouldBeFalse();
    }

    [Fact]
    public void A_request_with_the_railway_id_but_no_ip_headers_looks_wrong()
    {
        Read(("X-Railway-Request-Id", "abc123")).LooksWrong.ShouldBeTrue();
    }

    // O cliente pode mandar os cabeçalhos do Railway também: o log guarda no máximo 100 caracteres de cada.
    [Fact]
    public void Railway_ids_are_cut_to_a_short_length()
    {
        var report = Read(("X-Railway-Edge", new string('e', 500)), ("X-Railway-Request-Id", new string('r', 500)));

        report.Edge!.Length.ShouldBe(ClientIpHeaders.MaxIdLength);
        report.RailwayRequestId!.Length.ShouldBe(ClientIpHeaders.MaxIdLength);
    }
}
