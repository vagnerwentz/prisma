using System.Net;
using Prisma.Api.Infrastructure.Http;
using Shouldly;

namespace Prisma.Api.Tests.Http;

// Etapa S.1 (PLAN.md): endereço interno é o que nunca aparece como cliente vindo da internet. Se o IP do cliente que a
// API enxerga for um desses, ela está lendo o proxy, não a pessoa.
public sealed class ClientAddressTests
{
    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.10")]
    // A faixa dos saltos internos do Railway (e do CGNAT das operadoras, que nunca chega à internet).
    [InlineData("100.64.0.7")]
    [InlineData("100.127.255.255")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:10.1.2.3")]
    public void Private_and_proxy_addresses_are_internal(string address)
    {
        ClientAddress.IsInternal(IPAddress.Parse(address)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("203.0.113.10")]
    [InlineData("200.150.10.5")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("::ffff:203.0.113.10")]
    [InlineData("2001:db8:1:2::10")]
    public void Internet_addresses_are_not_internal(string address)
    {
        ClientAddress.IsInternal(IPAddress.Parse(address)).ShouldBeFalse();
    }
}
