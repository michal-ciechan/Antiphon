using System.Collections.Concurrent;

namespace Antiphon.HostCleanup.Checkpoints;

// No disk import or reset: only observations made by the creating context can add records.
public sealed class CheckpointCreationRegistry
{
    private readonly ConcurrentDictionary<string, byte> _created;
    private readonly ConcurrentDictionary<string, string> _content;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _freshRoots;
    private readonly ConcurrentDictionary<string, CheckpointStat> _identities;
    private readonly ConcurrentDictionary<string, CheckpointContentObservation> _mappingSeals;
    private readonly ConcurrentDictionary<string, byte> _sealedRoots;

    public CheckpointCreationRegistry(bool caseInsensitive = false)
    {
        var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _created = new(comparer);
        _content = new(comparer);
        _freshRoots = new(comparer);
        _identities = new(comparer);
        _mappingSeals = new(comparer);
        _sealedRoots = new(comparer);
    }

    private static string Key(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    public void RecordCreated(string path) => _created.TryAdd(Key(path), 0);
    public bool WasCreated(string path) => _created.ContainsKey(Key(path));
    public void RecordContent(string path, string content) => _content.TryAdd(Key(path), content);
    public string? Content(string path) => _content.TryGetValue(Key(path), out var value) ? value : null;
    public void RecordFreshRoot(string path, DateTimeOffset created) => _freshRoots.TryAdd(Key(path), created);
    public DateTimeOffset? FreshRoot(string path) => _freshRoots.TryGetValue(Key(path), out var value) ? value : null;
    public void RecordIdentity(string path, CheckpointStat stat) => _identities.TryAdd(Key(path), stat);
    public CheckpointStat? Identity(string path) => _identities.TryGetValue(Key(path), out var value) ? value : null;
    public bool BeginSeal(string root) => _sealedRoots.TryAdd(Key(root), 0);
    public void RecordMappingSeal(string path, CheckpointContentObservation observation) => _mappingSeals.TryAdd(Key(path), observation);
    public CheckpointContentObservation? MappingSeal(string path) => _mappingSeals.TryGetValue(Key(path), out var value) ? value : null;
}

public sealed record CheckpointContentObservation(CheckpointStat Stat, string Content);
