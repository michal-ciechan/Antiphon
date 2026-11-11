using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

// The child's only voice on stderr. Lines carry milliseconds since process start so a
// parent can place a stall. The config holds a database password: exception messages are
// never written, only exception types, SQL states and stack frames.
internal sealed class ProbeLog : ILoggerProvider, ILogger
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly double StartupMs =
        (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
    private readonly string _category;

    public ProbeLog(string category = "probe") => _category = category;

    public static void Phase(string text) => Write("phase " + text);

    private static void Write(string text) =>
        Console.Error.WriteLine($"probe +{StartupMs + Clock.Elapsed.TotalMilliseconds:F0}ms {text}");

    public ILogger CreateLogger(string categoryName) => new ProbeLog(categoryName);
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var types = new List<string>();
        for (var ex = exception; ex is not null; ex = ex.InnerException)
            types.Add(ex is DbException { SqlState: { } state1 } ? $"{ex.GetType().Name}({state1})" : ex.GetType().Name);
        Write($"{logLevel} {_category}: {formatter(state, null)}"
            + (types.Count == 0 ? "" : $" [{string.Join(" > ", types)}]{Environment.NewLine}{exception!.StackTrace}"));
    }

    public void Dispose() { }
}
