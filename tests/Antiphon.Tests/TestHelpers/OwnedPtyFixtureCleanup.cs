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
        _diagnose = diagnose ?? (_ => Task.FromResult("holder unknown: diagnosis unavailable"));
        var watch = Stopwatch.StartNew();
        _elapsed = elapsed ?? (() => watch.Elapsed);
        _delay = delay ?? ((span, ct) => Task.Delay(span, ct));
        _windows = windows ?? OperatingSystem.IsWindows();
    }

    internal Task DeleteAsync(CancellationToken ct = default) => _cleanup ??= DeleteCoreAsync(ct);

    private async Task DeleteCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(_root)) return;
        try { _deleteTree(_root); }
        catch
        {
            if (DenialObserved is not null) await DenialObserved();
            throw;
        }
        if (Directory.Exists(_root)) throw new IOException("Private tree still exists: " + _root);
    }
}
