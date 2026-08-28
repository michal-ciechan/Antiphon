using Microsoft.Extensions.Logging;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// Captures formatted log lines as <c>[Level] message</c>. Hoisted from
/// <c>TranscriptAdoptionSafetyTests</c> so CARD-0211 launch-shape tests can assert Warnings.
/// </summary>
internal sealed class ListLogger<T>(List<string> sink) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (sink)
            sink.Add($"[{logLevel}] {formatter(state, exception)}");
    }
}
