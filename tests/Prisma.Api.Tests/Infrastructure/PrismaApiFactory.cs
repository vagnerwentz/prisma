using Microsoft.AspNetCore.Mvc.Testing;

namespace Prisma.Api.Tests.Infrastructure;

public sealed class PrismaApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Default", connectionString);
}
