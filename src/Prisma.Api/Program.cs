using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Accounts;
using Prisma.Api.Features.Auth;
using Prisma.Api.Features.Categories;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Connection string 'Default' não configurada. Em desenvolvimento, use User Secrets.");

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton<IClock, SystemClock>();

// Enums trafegam como texto ("CreditCard"), nunca como número.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.AddProblemDetails();
builder.Services.AddAuth(builder.Configuration);
builder.Services.AddAuthFeatures();
builder.Services.AddAccountFeatures();
builder.Services.AddCategoryFeatures();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapCategoryEndpoints();

app.Run();
