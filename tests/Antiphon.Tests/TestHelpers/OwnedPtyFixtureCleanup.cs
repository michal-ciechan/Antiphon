using System.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

// Only the private tree and console images captured by this fixture may be deleted.
internal sealed class OwnedPtyFixtureCleanup
{
    internal sealed record Image(string Path, FileAttributes Attributes);
    private readonly string _root;
    private readonly Image[] _images;
    private readonly string _witnesses;
    private readonly Action<string> _deleteTree;
    private readonly Action<string> _deleteFile;
    private readonly Func<string, FileAttributes> _attributes;
    private readonly Func<string[], Task<string>> _diagnose;
    private readonly Func<TimeSpan> _elapsed;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly bool _windows;
    private Task? _cleanup;
    private readonly object _gate = new();
    internal Func<Task>? DenialObserved { get; set; }
    internal string? Diagnostic { get; private set; }

    internal OwnedPtyFixtureCleanup(string root, Image[] images, string witnesses,
        Action<string>? deleteTree = null, Action<string>? deleteFile = null,
        Func<string, FileAttributes>? attributes = null, Func<string[], Task<string>>? diagnose = null,
        Func<TimeSpan>? elapsed = null, Func<TimeSpan, CancellationToken, Task>? delay = null,
        bool? windows = null)
    {
        _root = Path.GetFullPath(root);
        _images = images;
        foreach (var image in images)
            if (!Path.GetFullPath(image.Path).StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(image.Path).Equals("OpenConsole.exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Console image is outside the owned tree");
        _witnesses = witnesses;
        _deleteTree = deleteTree ?? (path => Directory.Delete(path, true));
        _deleteFile = deleteFile ?? File.Delete;
        _attributes = attributes ?? File.GetAttributes;
        _diagnose = diagnose ?? WindowsFileLockOwners.QueryAsync;
        var watch = Stopwatch.StartNew();
        _elapsed = elapsed ?? (() => watch.Elapsed);
        _delay = delay ?? ((span, ct) => Task.Delay(span, ct));
        _windows = windows ?? OperatingSystem.IsWindows();
    }

    internal Task DeleteAsync(CancellationToken ct = default)
    {
        lock (_gate) return _cleanup ??= DeleteCoreAsync(ct);
    }

    private async Task DeleteCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(_root)) return;
        Exception first;
        Image? refused;
        try { _deleteTree(_root); AssertAbsent(); return; }
        catch (Exception ex) when (Candidate(ex))
        {
            first = ex;
            refused = AttributeRefusal();
            if (refused is null) throw;
        }
        string holders;
        try { holders = await _diagnose([refused.Path]); }
        catch (WindowsFileLockOwners.DiagnosticCleanupException ex)
        { throw new AggregateException($"Owned deletion of {refused.Path} and diagnostic cleanup both failed", first, ex); }
        catch (Exception ex) when (ex is not WindowsFileLockOwners.DiagnosticCleanupException)
        { holders = "holder unknown: " + ex; }
        Diagnostic = $"path={refused.Path} attributes={refused.Attributes}; {_witnesses}; {holders}";
        Console.WriteLine("C1515 first denial " + Diagnostic + $" hresult=0x{first.HResult:X8}");
        if (DenialObserved is not null) await DenialObserved();
        var start = _elapsed(); // The single diagnostic attempt precedes the five-second retry budget.
        var attempts = 1;
        Exception last = first;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = TimeSpan.FromSeconds(5) - (_elapsed() - start);
            if (remaining <= TimeSpan.Zero)
                throw new RefusalException(first, last, Diagnostic, attempts, _elapsed() - start);
            await _delay(remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50), ct);
            ct.ThrowIfCancellationRequested();
            attempts++;
            try { _deleteTree(_root); AssertAbsent(); return; }
            catch (Exception ex) when (Candidate(ex))
            {
                last = ex;
                if (AttributeRefusal() is null) throw;
            }
        }
    }

    private bool Candidate(Exception ex) => _windows && ex is IOException or UnauthorizedAccessException
        && ((uint)ex.HResult & 0xffff0000) == 0x80070000 && (ex.HResult & 0xffff) is 5 or 32 or 33;

    private Image? AttributeRefusal()
    {
        foreach (var image in _images)
        {
            if (!File.Exists(image.Path)) continue;
            var attributes = _attributes(image.Path);
            if (attributes != image.Attributes || (attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
                continue;
            try { _deleteFile(image.Path); }
            catch (Exception ex) when (Candidate(ex)) { return image; }
        }
        return null; // A basename in the recursive-delete error is insufficient attribution.
    }

    private void AssertAbsent()
    {
        if (Directory.Exists(_root)) throw new IOException("private-tree-deletable: " + _root);
    }

    internal sealed class RefusalException : IOException
    {
        internal Exception LastException { get; }
        internal RefusalException(Exception first, Exception last, string diagnostic, int attempts, TimeSpan elapsed)
            : base($"Owned deletion refused after {attempts} attempts, elapsed={elapsed}; {diagnostic}; first=0x{first.HResult:X8} {first.Message}; last=0x{last.HResult:X8} {last.Message}", first)
        {
            HResult = first.HResult;
            LastException = last;
        }
    }
}
