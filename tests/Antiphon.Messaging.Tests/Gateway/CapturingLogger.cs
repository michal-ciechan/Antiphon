using Microsoft.Extensions.Logging;

namespace Antiphon.Messaging.Tests.Gateway;

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Text)> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}
