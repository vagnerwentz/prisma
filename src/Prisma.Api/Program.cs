using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;
using Npgsql;
using Prisma.Api.Features.Accounts;
using Prisma.Api.Features.Recurrences;
using Prisma.Api.Features.Auth;
using Prisma.Api.Features.Categories;
using Prisma.Api.Features.Dashboard;
using Prisma.Api.Features.Diagnostics;
using Prisma.Api.Features.Statements;
using Prisma.Api.Features.Transactions;
using Prisma.Api.Features.Transfers;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

var builder = WebApplication.CreateBuilder(args);

// Sem criptografia GSS (Kerberos), que o Prisma não usa: o Npgsql tentaria antes do SSL e, na
// imagem de produção sem a biblioteca do Kerberos, registraria um erro no log.
var connectionString = new NpgsqlConnectionStringBuilder(
    builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Connection string 'Default' não configurada. Em desenvolvimento, use User Secrets."))
{
    GssEncryptionMode = GssEncryptionMode.Disable,
}.ConnectionString;

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

// Logs: o código fala só com o ILogger e os eventos de AppLog. A saída vem da configuração
// (Logging:Console:FormatterName): JSON de uma linha em produção, texto legível em desenvolvimento.
builder.Logging.AddConsoleFormatter<JsonLineConsoleFormatter, ConsoleFormatterOptions>();

// Toda resposta de erro leva o traceId da requisição, o mesmo dos logs dela: com ele, acha-se no
// Railway tudo o que aconteceu naquela chamada (@traceId:…).
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] =
        Activity.Current?.TraceId.ToHexString() ?? context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.AddAuth(builder.Configuration);
builder.Services.AddAuthFeatures();
builder.Services.AddAccountFeatures();
builder.Services.AddCategoryFeatures();
builder.Services.AddTransactionFeatures();
builder.Services.AddCardFeatures();
builder.Services.AddTransferFeatures();
builder.Services.AddDashboardFeatures();
builder.Services.AddRecurrenceFeatures(builder.Configuration);
builder.Services.AddDiagnosticsFeatures();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// Atrás do proxy da hospedagem, o IP e o esquema reais vêm nos cabeçalhos X-Forwarded-*: sem isso,
// o rate limit do login veria todos os usuários com o IP do proxy. Só com o proxy na frente, senão
// qualquer cliente forjaria o próprio IP. O proxy acrescenta o IP ao fim da lista, e só o último
// valor é lido (ForwardLimit = 1): o que o cliente escreve antes dele não conta.
if (builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

var app = builder.Build();

// Em produção, o contêiner aplica as migrations ao subir (uma instância só, sem corrida).
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;
    var pending = (await database.GetPendingMigrationsAsync()).Count();
    await database.MigrateAsync();
    app.Logger.MigrationsApplied(pending);
}

app.UseForwardedHeaders();
app.UseRequestLogging();

// Exceção não tratada e resposta de erro sem corpo (404 de rota inexistente, 401 do cookie) saem
// como ProblemDetails, o formato que o frontend lê.
app.UseExceptionHandler();
app.UseStatusCodePages();

// O frontend fica fora de /api; o roteamento vem depois, para enxergar as rotas sem o prefixo.
app.UseFrontendAndApiPrefix();
app.UseRouting();

app.UseAuthentication();
app.UseUserLogScope();
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
app.MapTransferEndpoints();
app.MapRecurrenceEndpoints();
app.MapDashboardEndpoints();
app.MapDiagnosticsEndpoints();

app.Run();
