using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Prisma.Domain;

namespace Prisma.Api.Infrastructure.Logging;

// Uma linha JSON por evento, no formato que o Railway lê e filtra: `level` (debug, info, warn, error) e
// `message` reconhecidos, as propriedades do evento no primeiro nível (@userId:…, @traceId:…). O
// formatador JSON do .NET escreve `LogLevel` e `Message` e aninha as propriedades em `State`, o que o
// Railway não entende. Dos escopos, só o que identifica a requisição e a pessoa: o caminho cru da
// requisição (RequestPath) pode levar ids e consulta.
public sealed class JsonLineConsoleFormatter(IClock clock) : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "json-line";

    private static readonly HashSet<string> ScopeKeys = ["TraceId", "SpanId", "UserId"];

    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
            return;

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, Options))
        {
            var written = new HashSet<string>(StringComparer.Ordinal)
                { "timestamp", "level", "message", "category", "eventId", "eventName", "exception" };

            json.WriteStartObject();
            json.WriteString("timestamp", clock.UtcNow);
            json.WriteString("level", LevelOf(logEntry.LogLevel));
            json.WriteString("message", message);
            json.WriteString("category", logEntry.Category);
            if (logEntry.EventId.Id != 0)
                json.WriteNumber("eventId", logEntry.EventId.Id);
            if (!string.IsNullOrEmpty(logEntry.EventId.Name))
                json.WriteString("eventName", logEntry.EventId.Name);

            if (logEntry.State is IEnumerable<KeyValuePair<string, object?>> properties)
                foreach (var (key, value) in properties)
                    if (key != "{OriginalFormat}")
                        WriteProperty(json, written, key, value);

            scopeProvider?.ForEachScope((scope, state) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    foreach (var (key, value) in pairs)
                        if (ScopeKeys.Contains(key))
                            WriteProperty(state.Json, state.Written, key, value);
            }, (Json: json, Written: written));

            if (logEntry.Exception is not null)
                json.WriteString("exception", logEntry.Exception.ToString());
            json.WriteEndObject();
        }

        textWriter.Write(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
        textWriter.Write(Environment.NewLine);
    }

    private static string LevelOf(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        _ => "error",
    };

    // O primeiro que escreve uma chave fica com ela: o evento vence o escopo.
    private static void WriteProperty(Utf8JsonWriter json, HashSet<string> written, string key, object? value)
    {
        var name = char.ToLowerInvariant(key[0]) + key[1..];
        if (!written.Add(name))
            return;

        switch (value)
        {
            case null:
                json.WriteNull(name);
                break;
            case bool flag:
                json.WriteBoolean(name, flag);
                break;
            case int or long or short or byte or double or float or decimal:
                json.WriteNumber(name, Convert.ToDecimal(value, CultureInfo.InvariantCulture));
                break;
            default:
                json.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
