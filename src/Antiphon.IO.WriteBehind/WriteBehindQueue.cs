using System.IO.Abstractions;

namespace Antiphon.IO.WriteBehind;

/// <summary>
/// The pending whole-file writes of one <see cref="WriteBehindFileSystem"/> and the thread that lands them.
/// A path written again before its earlier content landed replaces that content and moves to the back of
/// the queue, so files land in the order they were last written and only the newest content of each is
/// written. The content being written stays readable until it is on disk.
/// </summary>
internal sealed class WriteBehindQueue
{
    private readonly IFileSystem inner;
    private readonly WriteBehindOptions options;
    private readonly object gate = new();
    private readonly LinkedList<string> order = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, byte[] Content)> pending;
    private readonly List<WriteBehindException> failures = new();
    private string? writingPath;
    private byte[]? writingContent;
    private bool running;
    private bool disposed;

    public WriteBehindQueue(IFileSystem inner, WriteBehindOptions options)
    {
        this.inner = inner;
        this.options = options;
        pending = new Dictionary<string, (LinkedListNode<string>, byte[])>(options.PathComparer);
    }

    /// <summary>Test seam: runs on the writer thread before each write lands.</summary>
    internal Action<string>? BeforeWrite { get; set; }

    /// <summary>Test seam: runs on the writer thread after each write landed.</summary>
    internal Action<string>? AfterWrite { get; set; }

    public string Key(string path) => inner.Path.GetFullPath(path);

    public void Enqueue(string path, byte[] content)
    {
        string key = Key(path);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, typeof(WriteBehindFileSystem));
            if (pending.TryGetValue(key, out var old)) order.Remove(old.Node);
            pending[key] = (order.AddLast(key), content);
            if (!running)
            {
                running = true;
                var thread = new Thread(Run) { IsBackground = !options.KeepProcessAlive, Name = "write-behind" };
                thread.Start();
            }
        }
    }

    /// <summary>The newest content written for a path that is not on disk yet.</summary>
    public bool TryGet(string path, out byte[] content)
    {
        string key = Key(path);
        lock (gate)
        {
            if (pending.TryGetValue(key, out var entry)) { content = entry.Content; return true; }
            if (writingPath is not null && options.PathComparer.Equals(writingPath, key)) { content = writingContent!; return true; }
        }
        content = Array.Empty<byte>();
        return false;
    }

    /// <summary>Drops a queued write that has not started, then waits for one in progress.</summary>
    public void Cancel(string path)
    {
        string key = Key(path);
        lock (gate)
        {
            if (pending.Remove(key, out var entry)) order.Remove(entry.Node);
            while (writingPath is not null && options.PathComparer.Equals(writingPath, key)) Monitor.Wait(gate);
        }
    }

    /// <summary>Waits until the path's queued content is on disk.</summary>
    public void Flush(string path)
    {
        string key = Key(path);
        lock (gate)
        {
            while (pending.ContainsKey(key) || (writingPath is not null && options.PathComparer.Equals(writingPath, key))) Monitor.Wait(gate);
            ThrowFailures();
        }
    }

    /// <summary>Waits until every queued write is on disk.</summary>
    public void Flush()
    {
        lock (gate)
        {
            while (order.Count > 0 || writingPath is not null) Monitor.Wait(gate);
            ThrowFailures();
        }
    }

    public int PendingCount
    {
        get { lock (gate) return order.Count + (writingPath is null ? 0 : 1); }
    }

    public void Dispose()
    {
        try { Flush(); }
        finally { lock (gate) disposed = true; }
    }

    private void ThrowFailures()
    {
        if (failures.Count == 0) return;
        var failed = failures.ToArray();
        failures.Clear();
        throw new AggregateException("Write-behind writes failed: " + string.Join(", ", failed.Select(f => f.Path)), failed);
    }

    private void Run()
    {
        while (true)
        {
            string path;
            byte[] content;
            lock (gate)
            {
                if (order.Count == 0)
                {
                    running = false;
                    Monitor.PulseAll(gate);
                    return;
                }
                path = order.First!.Value;
                order.RemoveFirst();
                content = pending[path].Content;
                pending.Remove(path);
                writingPath = path;
                writingContent = content;
            }
            try
            {
                BeforeWrite?.Invoke(path);
                Land(path, content);
                AfterWrite?.Invoke(path);
            }
            catch (Exception ex)
            {
                var failure = new WriteBehindException(path, ex);
                lock (gate) failures.Add(failure);
                try { options.OnWriteFailed?.Invoke(failure); } catch { }
            }
            finally
            {
                lock (gate)
                {
                    writingPath = null;
                    writingContent = null;
                    Monitor.PulseAll(gate);
                }
            }
        }
    }

    private void Land(string path, byte[] content)
    {
        if (!options.AtomicReplace)
        {
            inner.File.WriteAllBytes(path, content);
            return;
        }
        // A temporary file beside the target, then a rename over it: a reader sees the old or the new file.
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            inner.File.WriteAllBytes(temp, content);
            inner.File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (inner.File.Exists(temp)) inner.File.Delete(temp); } catch { }
            throw;
        }
    }
}
