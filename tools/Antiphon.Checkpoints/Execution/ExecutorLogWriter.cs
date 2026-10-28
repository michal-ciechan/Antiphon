using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Antiphon.Checkpoints;

public interface IExecutorLogSink : IAsyncDisposable
{
    Task AppendAsync(string line, CancellationToken cancellationToken);
    Task FlushAsync(CancellationToken cancellationToken);
}

internal sealed class FileExecutorLogSink : IExecutorLogSink
{
    private readonly StreamWriter _writer;
    public FileExecutorLogSink(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var reason = SafeWrite.OpenAppend(path, SafeWrite.Under(directory), out var stream);
        if (reason is not null || stream is null)
            throw new IOException("refused: " + reason);
        _writer = new StreamWriter(stream);
    }

    // SafeWrite opens a non-overlapped handle. WriteLineAsync on that handle
    // queues the write to the thread pool; Note must not block on that task.
    public Task AppendAsync(string line, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _writer.WriteLine(line);
        _writer.Flush();
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _writer.Flush();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _writer.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class ExecutorLogWriter : IAsyncDisposable
{
    private readonly IExecutorLogSink _sink;
    private readonly BlockingCollection<LogItem> _queue = new();
    private readonly object _admit = new();
    private readonly Thread _thread;
    private bool _closed;
    private Task? _disposed;

    public ExecutorLogWriter(IExecutorLogSink sink)
    {
        _sink = sink;
        _thread = new Thread(WriteLoop) { IsBackground = true, Name = "executor-log" };
        _thread.Start();
    }

    public void Note(string line)
    {
        using var gate = new ManualResetEventSlim(false);
        var item = Admit(LogKind.Append, line, CancellationToken.None, gate);
        // The writer thread runs the sink and signals this event afterwards.
        // Note does not block on the sink task, so a continuation is not stuck
        // behind this caller.
        gate.Wait();
        if (item.Error is not null)
            ExceptionDispatchInfo.Capture(item.Error).Throw();
    }

    public Task AppendAsync(string line, CancellationToken cancellationToken = default)
        => Admit(LogKind.Append, line, cancellationToken, gate: null).Done.Task;

    public Task FlushAsync(CancellationToken cancellationToken = default)
        => Admit(LogKind.Flush, line: null, cancellationToken, gate: null).Done.Task;

    public ValueTask DisposeAsync()
    {
        Task disposed;
        lock (_admit)
        {
            if (_disposed is not null)
                disposed = _disposed;
            else
            {
                var item = new LogItem(LogKind.Dispose, line: null, CancellationToken.None, gate: null);
                _closed = true;
                _queue.Add(item);
                _disposed = item.Done.Task;
                return new ValueTask(_disposed);
            }
        }

        // The first caller owns a sink fault. A repeat joins that one disposal and completes.
        return new ValueTask(JoinWithoutFault(disposed));
    }

    private static async Task JoinWithoutFault(Task disposed)
    {
        try
        {
            await disposed.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Observed by the first DisposeAsync.
        }
    }

    private LogItem Admit(LogKind kind, string? line, CancellationToken cancellationToken, ManualResetEventSlim? gate)
    {
        var item = new LogItem(kind, line, cancellationToken, gate);
        lock (_admit)
        {
            if (_closed)
                throw new ObjectDisposedException(nameof(ExecutorLogWriter));
            _queue.Add(item);
        }
        return item;
    }

    private void WriteLoop()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                Run(item);
                if (item.Kind == LogKind.Dispose)
                    break;
            }
        }
        catch (Exception ex)
        {
            while (_queue.TryTake(out var item))
                item.Finish(ex);
        }
    }

    private void Run(LogItem item)
    {
        try
        {
            if (item.Kind != LogKind.Dispose && item.Cancellation.IsCancellationRequested)
                throw new OperationCanceledException(item.Cancellation);
            switch (item.Kind)
            {
                case LogKind.Append:
                    _sink.AppendAsync(Stamp(item.Line!), item.Cancellation).GetAwaiter().GetResult();
                    break;
                case LogKind.Flush:
                    _sink.FlushAsync(item.Cancellation).GetAwaiter().GetResult();
                    break;
                case LogKind.Dispose:
                    _sink.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    break;
            }
            item.Finish(null);
        }
        catch (Exception ex)
        {
            item.Finish(ex);
        }
    }

    private static string Stamp(string line) => DateTimeOffset.UtcNow.ToString("o") + " " + line;

    private enum LogKind { Append, Flush, Dispose }

    private sealed class LogItem
    {
        public LogItem(LogKind kind, string? line, CancellationToken cancellation, ManualResetEventSlim? gate)
        {
            Kind = kind;
            Line = line;
            Cancellation = cancellation;
            Gate = gate;
        }

        public LogKind Kind { get; }
        public string? Line { get; }
        public CancellationToken Cancellation { get; }
        public ManualResetEventSlim? Gate { get; }
        public TaskCompletionSource<bool> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Error { get; private set; }

        public void Finish(Exception? error)
        {
            Error = error;
            if (error is null)
                Done.TrySetResult(true);
            else if (error is OperationCanceledException oce)
                Done.TrySetCanceled(oce.CancellationToken);
            else
                Done.TrySetException(error);
            Gate?.Set();
        }
    }
}
