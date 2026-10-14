using FluentValidation;
using Prisma.Api.Infrastructure.Email;

namespace Prisma.Api.Features.Auth;

public static class AuthModule
{
    public static IServiceCollection AddAuthFeatures(this IServiceCollection services)
    {
        services.AddScoped<Register.Handler>();
        services.AddScoped<Login.Handler>();
        services.AddScoped<Me.Handler>();
        services.AddScoped<ConfirmEmail.Handler>();
        services.AddScoped<ResendConfirmation.Handler>();
        services.AddScoped<IEmailComposer, ConfirmEmailComposer>();
        services.AddScoped<IEmailComposer, AlreadyRegisteredComposer>();

        services.AddSingleton<IValidator<Register.Request>, Register.Validator>();
        services.AddSingleton<IValidator<Login.Request>, Login.Validator>();
        services.AddSingleton<IValidator<ConfirmEmail.Request>, ConfirmEmail.Validator>();
        services.AddSingleton<IValidator<ResendConfirmation.Request>, ResendConfirmation.Validator>();

        return services;
    }

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");
        Register.Map(group);
        Login.Map(group);
        Logout.Map(group);
        Me.Map(group);
        ConfirmEmail.Map(group);
        ResendConfirmation.Map(group);
    }
}
