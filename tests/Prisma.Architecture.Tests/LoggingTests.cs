using System.Reflection;
using Microsoft.Extensions.Logging;
using Prisma.Api.Infrastructure.Logging;
using Shouldly;

namespace Prisma.Architecture.Tests;

// Eventos de log declarados num lugar só, com [LoggerMessage] (etapa H.3a). A regra 8 do CLAUDE.md
// (nunca logar valor, descrição, e-mail, senha ou token) deixa de depender de lembrança: um evento com
// uma propriedade assim não passa. O uso de LogX fora dos eventos é barrado pelo compilador (CA1848).
public sealed class LoggingTests
{
    private static readonly string[] SensitiveWords =
        ["amount", "cents", "balance", "limit", "description", "name", "email", "password", "token"];

    private static readonly (MethodInfo Method, LoggerMessageAttribute Attribute)[] Events = typeof(AppLog).Assembly
        .GetTypes()
        .SelectMany(type => type.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        .Select(method => (method, attribute: method.GetCustomAttribute<LoggerMessageAttribute>()))
        .Where(pair => pair.attribute is not null)
        .Select(pair => (pair.method, pair.attribute!))
        .ToArray();

    [Fact]
    public void The_api_declares_its_log_events()
    {
        Events.ShouldNotBeEmpty();
        Events.ShouldAllBe(e => e.Method.DeclaringType == typeof(AppLog));
    }

    [Fact]
    public void Event_ids_are_unique()
    {
        var duplicated = Events.GroupBy(e => e.Attribute.EventId).Where(g => g.Count() > 1).Select(g => g.Key);
        duplicated.ShouldBeEmpty();
    }

    [Fact]
    public void No_event_carries_a_sensitive_property()
    {
        var violations = Events
            .SelectMany(e => e.Method.GetParameters()
                .Where(p => p.ParameterType != typeof(ILogger) && !typeof(Exception).IsAssignableFrom(p.ParameterType))
                .Where(p => SensitiveWords.Any(word => p.Name!.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Select(p => $"{e.Method.Name}({p.Name})"))
            .ToList();

        violations.ShouldBeEmpty();
    }
}
