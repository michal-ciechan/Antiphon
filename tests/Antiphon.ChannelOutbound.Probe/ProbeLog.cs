using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

// The child's only voice on stderr. Lines carry milliseconds since process start so a
// parent can place a stall; in the test host (an in-process recovery) that is the host's age, and a stall is
// placed by the run-start line instead. The config holds a database password: exception messages are
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
        Write($"{logLevel} {_category}: {formatter(state, null)}"
            + (exception is null ? "" : $" [{Types(exception)}]{Environment.NewLine}{exception.StackTrace}"));
    }

    // An exception as the probe may report it: the chain's types (and SQL states), never a message.
    internal static string Types(Exception exception)
    {
        var types = new List<string>();
        for (var ex = exception; ex is not null; ex = ex.InnerException)
            types.Add(ex is DbException { SqlState: { } state } ? $"{ex.GetType().Name}({state})" : ex.GetType().Name);
        return string.Join(" > ", types);
    }

    // The types, then every exception's own frames (each inner one too, and each of an aggregate's), no messages.
    internal static string Describe(Exception exception)
    {
        var text = new System.Text.StringBuilder(Types(exception));
        var pending = new Stack<Exception>();
        pending.Push(exception);
        while (pending.Count > 0)
        {
            var ex = pending.Pop();
            text.Append(Environment.NewLine).Append("--- ").Append(ex.GetType().Name).Append(Environment.NewLine)
                .Append(ex.StackTrace);
            if (ex is AggregateException aggregate)
                for (var i = aggregate.InnerExceptions.Count - 1; i >= 0; i--) pending.Push(aggregate.InnerExceptions[i]);
            else if (ex.InnerException is { } inner) pending.Push(inner);
        }
        return text.ToString();
    }

    public void Dispose() { }
}
