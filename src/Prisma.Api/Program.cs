using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Accounts;
using Prisma.Api.Features.Auth;
using Prisma.Api.Features.Categories;
using Prisma.Api.Features.Statements;
using Prisma.Api.Features.Transactions;
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

// Enums trafegam como texto ("CreditCard"), nunca como número. Números só como número: o padrão
// web aceitaria "12990" em texto para um valor em centavos.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

// Documento OpenAPI para gerar os tipos do frontend (npm run gen:api). Tipos aninhados como
// Register.Response viram "RegisterResponse", para não colidirem entre si.
builder.Services.AddOpenApi(options => options.CreateSchemaReferenceId = type =>
    type.Type.IsNested
        ? $"{type.Type.DeclaringType!.Name}{type.Type.Name}"
        : OpenApiOptions.CreateDefaultSchemaReferenceId(type));

builder.Services.AddProblemDetails();
builder.Services.AddAuth(builder.Configuration);
builder.Services.AddAuthFeatures();
builder.Services.AddAccountFeatures();
builder.Services.AddCategoryFeatures();
builder.Services.AddTransactionFeatures();
builder.Services.AddCardFeatures();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapCategoryEndpoints();
app.MapTransactionEndpoints();
app.MapCardEndpoints();

app.Run();
