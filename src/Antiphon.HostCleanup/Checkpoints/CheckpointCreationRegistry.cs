using System.Collections.Concurrent;

namespace Antiphon.HostCleanup.Checkpoints;

// No disk import or reset: only observations made by the creating context can add records.
public sealed class CheckpointCreationRegistry
{
    private readonly ConcurrentDictionary<string, byte> _created;
    private readonly ConcurrentDictionary<string, string> _content;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _freshRoots;
    private readonly ConcurrentDictionary<string, FilesystemIdentity> _identity;

    public CheckpointCreationRegistry(bool caseInsensitive = false)
    {
        var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _created = new(comparer);
        _content = new(comparer);
        _freshRoots = new(comparer);
        _identity = new(comparer);
    }

    private static string Key(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    public void RecordCreated(string path) => _created.TryAdd(Key(path), 0);
    public bool WasCreated(string path) => _created.ContainsKey(Key(path));
    public void RecordContent(string path, string content) => _content.TryAdd(Key(path), content);
    public string? Content(string path) => _content.TryGetValue(Key(path), out var value) ? value : null;
    public void RecordFreshRoot(string path, DateTimeOffset created) => _freshRoots.TryAdd(Key(path), created);
    public DateTimeOffset? FreshRoot(string path) => _freshRoots.TryGetValue(Key(path), out var value) ? value : null;
    // First bound identity wins. A later replacement at the same path must not inherit custody.
    public void RecordIdentity(string path, FilesystemIdentity identity)
    {
        if (identity.IsBound) _identity.TryAdd(Key(path), identity);
    }
    public FilesystemIdentity? Identity(string path) => _identity.TryGetValue(Key(path), out var value) ? value : null;
}
