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
        services.AddScoped<ForgotPassword.Handler>();
        services.AddScoped<ResetPassword.Handler>();
        services.AddScoped<ChangePassword.Handler>();
        services.AddScoped<DeleteMyAccount.Handler>();
        services.AddScoped<AcceptTerms.Handler>();
        services.AddScoped<IEmailComposer, ConfirmEmailComposer>();
        services.AddScoped<IEmailComposer, AlreadyRegisteredComposer>();
        services.AddScoped<IEmailComposer, ResetPasswordComposer>();
        services.AddScoped<IEmailComposer, PasswordChangedComposer>();

        services.AddSingleton<IValidator<Register.Request>, Register.Validator>();
        services.AddSingleton<IValidator<Login.Request>, Login.Validator>();
        services.AddSingleton<IValidator<ConfirmEmail.Request>, ConfirmEmail.Validator>();
        services.AddSingleton<IValidator<ResendConfirmation.Request>, ResendConfirmation.Validator>();
        services.AddSingleton<IValidator<ForgotPassword.Request>, ForgotPassword.Validator>();
        services.AddSingleton<IValidator<ResetPassword.Request>, ResetPassword.Validator>();
        services.AddSingleton<IValidator<ChangePassword.Request>, ChangePassword.Validator>();
        services.AddSingleton<IValidator<DeleteMyAccount.Request>, DeleteMyAccount.Validator>();

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
        ForgotPassword.Map(group);
        ResetPassword.Map(group);
        ChangePassword.Map(group);
        DeleteMyAccount.Map(group);
        AcceptTerms.Map(group);
    }
}
