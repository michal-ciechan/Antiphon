using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Antiphon.Tests.Review;

internal sealed class ReviewTestLog : ILoggerProvider, ILogger
{
    public ConcurrentQueue<string> Warnings { get; } = new();

    public ILogger CreateLogger(string categoryName) => this;
    public void Dispose() { }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
            Warnings.Enqueue(formatter(state, exception));
    }
}
