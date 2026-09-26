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
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete));
    }
    public async Task AppendAsync(string line, CancellationToken cancellationToken)
    {
        await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    public Task FlushAsync(CancellationToken cancellationToken) => _writer.FlushAsync(cancellationToken);
    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}

public sealed class ExecutorLogWriter : IAsyncDisposable
{
    private readonly IExecutorLogSink _sink;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public ExecutorLogWriter(IExecutorLogSink sink) => _sink = sink;

    public async Task AppendAsync(string line, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ExecutorLogWriter));
            await _sink.AppendAsync(DateTimeOffset.UtcNow.ToString("o") + " " + line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Note(string line) => AppendAsync(line).GetAwaiter().GetResult();

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ExecutorLogWriter));
            await _sink.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            await _sink.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
