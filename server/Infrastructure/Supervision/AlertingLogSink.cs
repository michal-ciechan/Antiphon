using System.Collections.Concurrent;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Serilog.Core;
using Serilog.Events;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>
/// The log tap (spec part B, Q8 — ships DISABLED): forwards Warning+ Serilog events into the
/// alert pipeline. Two hard rules: (1) the alert pipeline's own sources are excluded so a warning
/// while raising an alert can never loop; (2) a per-message-template rate limit runs BEFORE the
/// router's dedup so a log storm cannot even reach the pipeline.
/// Registered as a static instance (Serilog config runs before DI); Attach() arms it after build.
/// </summary>
public sealed class AlertingLogSink : ILogEventSink
{
    public static readonly AlertingLogSink Instance = new();

    private static readonly string[] ExcludedSourcePrefixes =
    [
        "Antiphon.Server.Application.Services.AlertService",
        "Antiphon.Server.Application.Services.ChannelAlertRouter",
        "Antiphon.Server.Application.Services.AlertDigestFlusher",
        "Antiphon.Server.Infrastructure.Supervision.AlertDigestFlushHostedService",
        "Antiphon.Messaging",
    ];

    private static readonly TimeSpan PerTemplateLimit = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, DateTime> _lastSentByTemplate = new();
    private IServiceProvider? _services;
    private bool _enabled;
    private LogEventLevel _minLevel = LogEventLevel.Warning;

    private AlertingLogSink()
    {
    }

    public void Attach(IServiceProvider services, bool enabled, string minLevel)
    {
        _services = services;
        _enabled = enabled;
        _minLevel = Enum.TryParse<LogEventLevel>(minLevel, ignoreCase: true, out var level)
            ? level
            : LogEventLevel.Warning;
    }

    public void Emit(LogEvent logEvent)
    {
        if (!_enabled || _services is null || logEvent.Level < _minLevel)
            return;

        if (logEvent.Properties.TryGetValue("SourceContext", out var sourceValue)
            && sourceValue is ScalarValue { Value: string source }
            && ExcludedSourcePrefixes.Any(source.StartsWith))
        {
            return;
        }

        var template = logEvent.MessageTemplate.Text;
        var now = DateTime.UtcNow;
        var last = _lastSentByTemplate.GetOrAdd(template, DateTime.MinValue);
        if (now - last < PerTemplateLimit || !_lastSentByTemplate.TryUpdate(template, now, last))
            return;

        var services = _services;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IAlertService>().RaiseAsync(
                    new AlertRaise(
                        logEvent.Level >= LogEventLevel.Error ? AlertSeverity.Error : AlertSeverity.Warning,
                        Source: "log",
                        Title: Truncate(logEvent.RenderMessage(), 200),
                        Detail: logEvent.Exception?.Message,
                        DedupKey: $"log:{template.GetHashCode():x8}"),
                    CancellationToken.None);
            }
            catch
            {
                // The tap is best-effort by definition.
            }
        });
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
