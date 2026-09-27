using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Prisma.Api.Tests.Infrastructure;

public sealed record CapturedLog(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Exception? Exception,
    string? TraceId)
{
    public object? this[string property] => Properties.GetValueOrDefault(property);
}

// Guarda os logs da API em memória, com o traceId da requisição em que foram escritos. As propriedades
// dos escopos (o userId da requisição) entram junto, atrás das do evento.
public sealed class LogSink : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();
    private IExternalScopeProvider? _scopes;

    public IReadOnlyCollection<CapturedLog> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries, () => _scopes);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    // A linha da requisição é escrita quando o pipeline termina, que pode ser logo depois de a
    // resposta chegar ao cliente.
    public async Task<CapturedLog> WaitFor(Func<CapturedLog, bool> predicate)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var found = _entries.FirstOrDefault(predicate);
            if (found is not null)
                return found;
            await Task.Delay(50);
        }

        throw new ShouldAssertException(
            "Nenhum log atende à condição. Logs: " + string.Join(" | ", _entries.Select(e => $"{e.EventId.Name}: {e.Message}")));
    }

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<CapturedLog> entries, Func<IExternalScopeProvider?> scopes) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.Where(p => p.Key != "{OriginalFormat}").ToDictionary(p => p.Key, p => p.Value)
                : new Dictionary<string, object?>();
            scopes()?.ForEachScope((scope, target) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> scoped)
                    foreach (var (key, value) in scoped)
                        target.TryAdd(key, value);
            }, properties);
            entries.Enqueue(new CapturedLog(category, logLevel, eventId, formatter(state, exception), properties, exception,
                Activity.Current?.TraceId.ToHexString()));
        }
    }
}
