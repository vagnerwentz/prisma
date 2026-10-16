using System.Net;
using System.Net.Sockets;

namespace Prisma.Api.Infrastructure.Http;

// Endereços que nunca aparecem como cliente vindo da internet (etapa S.1): rede privada, a faixa dos saltos internos do
// Railway e do CGNAT das operadoras (100.64.0.0/10), loopback e link-local. Se o IP do cliente que a API enxerga for um
// desses, ela está lendo o proxy, não a pessoa, e o rate limit por IP junta gente diferente.
public static class ClientAddress
{
    public static bool IsInternal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && (b[1] & 0xF0) == 16)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && (b[1] & 0xC0) == 64)
                || (b[0] == 169 && b[1] == 254);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
    }
}
