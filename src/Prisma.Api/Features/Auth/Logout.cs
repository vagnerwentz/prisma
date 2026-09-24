using Microsoft.AspNetCore.Identity;
using Prisma.Api.Infrastructure.Auth;

namespace Prisma.Api.Features.Auth;

public static class Logout
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        })
            .Produces(204);
}
