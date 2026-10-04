namespace Antiphon.Tests.TestHelpers;

/// <summary>A byte image of one exact, fixture-owned root. Never follows filesystem links.</summary>
internal sealed class CheckpointSourceFixtureImage
{
    private sealed record Entry(string Path, byte[]? Bytes, FileAttributes Attributes, UnixFileMode? Mode);
    private readonly string _root;
    private readonly Entry[] _entries;

    private CheckpointSourceFixtureImage(string root, Entry[] entries) { _root = root; _entries = entries; }

    public static CheckpointSourceFixtureImage Capture(string root)
    {
        root = Path.GetFullPath(root);
        var entries = ReadTree(root).Select(path => new Entry(Path.GetRelativePath(root, path),
            Directory.Exists(path) ? null : File.ReadAllBytes(path), File.GetAttributes(path),
            OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(path))).ToArray();
        return new(root, entries);
    }

    private static IEnumerable<string> ReadTree(string root)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("linked fixture image");
        yield return root;
        if (!Directory.Exists(root)) yield break;
        foreach (var child in Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal))
            foreach (var path in ReadTree(child)) yield return path;
    }

    public void ValidateTarget(string root, Action destructiveAction)
    {
        if (!string.Equals(Path.GetFullPath(root), _root, StringComparison.Ordinal))
            throw new InvalidOperationException("foreign fixture image");
        if (Directory.Exists(root)) _ = ReadTree(root).ToArray();
        destructiveAction();
    }

    public bool Matches()
    {
        if (!Directory.Exists(_root)) return false;
        var paths = ReadTree(_root).Select(p => Path.GetRelativePath(_root, p)).ToHashSet(StringComparer.Ordinal);
        if (!paths.SetEquals(_entries.Select(e => e.Path))) return false;
        foreach (var entry in _entries)
        {
            var path = Path.Combine(_root, entry.Path);
            if (File.GetAttributes(path) != entry.Attributes) return false;
            if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(path) != entry.Mode) return false;
            if (entry.Bytes is not null && !File.ReadAllBytes(path).AsSpan().SequenceEqual(entry.Bytes)) return false;
        }
        return true;
    }

    public void Restore()
    {
        ValidateTarget(_root, () =>
        {
            if (Directory.Exists(_root))
            {
                foreach (var path in ReadTree(_root)) File.SetAttributes(path, FileAttributes.Normal);
                // Keep the root inode: the standing script worker may have this exact directory as its idle CWD.
                foreach (var child in Directory.EnumerateFileSystemEntries(_root))
                    if (Directory.Exists(child)) Directory.Delete(child, true);
                    else File.Delete(child);
            }
            foreach (var entry in _entries.Where(e => e.Bytes is null))
                Directory.CreateDirectory(Path.Combine(_root, entry.Path));
            foreach (var entry in _entries.Where(e => e.Bytes is not null))
                File.WriteAllBytes(Path.Combine(_root, entry.Path), entry.Bytes!);
            foreach (var entry in _entries.Reverse())
            {
                var path = Path.Combine(_root, entry.Path);
                if (!OperatingSystem.IsWindows() && entry.Mode is UnixFileMode mode) File.SetUnixFileMode(path, mode);
                File.SetAttributes(path, entry.Attributes);
            }
        });
    }
}
