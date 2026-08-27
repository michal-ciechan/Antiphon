using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// How hard an intersection in one area is. A DOWNGRADE only: <see cref="Allow"/> can never make a
/// pair of tasks stricter than their workspace pair already makes them (CARD-0063 §2.3).
/// </summary>
public enum AreaWeight
{
    /// <summary>The default. The workspace pair decides what the intersection costs.</summary>
    Serialise = 0,

    /// <summary>
    /// Not worth waiting or warning for on its own. <c>docs</c> is the only one in v1: nearly every
    /// card appends to <c>AGENTS.md</c>, and serialising on that would serialise the fleet.
    /// </summary>
    Allow = 1,
}

/// <summary>One named area of a repo: the paths it owns and what an intersection in it costs.</summary>
public sealed record AreaDefinition(string Name, IReadOnlyList<string> Paths, AreaWeight Weight);

/// <summary>
/// A repo's areas, as read from its <c>antiphon.areas.json</c>. A repo without one gets
/// <see cref="Empty"/> — every scope token is then read as a path, which is exactly the behaviour
/// that predates the map.
/// </summary>
public sealed class AreaMap
{
    public static AreaMap Empty { get; } = new(new Dictionary<string, AreaDefinition>(), null);

    private readonly IReadOnlyDictionary<string, AreaDefinition> _areas;

    public AreaMap(IReadOnlyDictionary<string, AreaDefinition> areas, string? sourcePath)
    {
        _areas = areas;
        SourcePath = sourcePath;
    }

    /// <summary>Absolute path the map came from; null for <see cref="Empty"/>.</summary>
    public string? SourcePath { get; }

    public int Count => _areas.Count;

    /// <summary>Area names in declaration order, for the "known names are …" half of a warning.</summary>
    public IReadOnlyList<string> Names => _areas.Values.Select(a => a.Name).ToList();

    public IReadOnlyCollection<AreaDefinition> Areas => _areas.Values.ToList();

    public bool TryGet(string name, out AreaDefinition area) => _areas.TryGetValue(name, out area!);
}

/// <summary>
/// Reads and caches each repo's <c>antiphon.areas.json</c> (CARD-0063 S2).
///
/// <para>The map is a fact about a REPO's layout, not about the server, which is why it is a
/// tracked file at the repo root rather than configuration: tasks carry their own
/// <c>RepoPath</c> precisely so cross-repo orchestration works, and a server-global map would be
/// wrong for the second repo the day it is used.</para>
///
/// <para><b>It can never fail a dispatch.</b> A missing file, a malformed file, an unreadable file
/// — each logs and yields <see cref="AreaMap.Empty"/>, which degrades exactly to the pre-map
/// behaviour (names compare as opaque labels). A bookkeeping field must not be able to refuse a
/// launch.</para>
/// </summary>
public sealed class AreaMapLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly DelegationSettings _settings;
    private readonly ILogger<AreaMapLoader> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public AreaMapLoader(IOptions<DelegationSettings> settings, ILogger<AreaMapLoader> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// The areas declared by the repo at <paramref name="repoPath"/>. Cached per path and
    /// invalidated by the file's write time and length, so an edit to the map is live on the next
    /// tick without a restart — and a repo with no map costs one negative cache hit.
    /// </summary>
    public AreaMap Load(string? repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
            return AreaMap.Empty;

        string file;
        try
        {
            file = Path.Combine(repoPath, _settings.AreasFileName);
        }
        catch (ArgumentException)
        {
            return AreaMap.Empty;
        }

        var info = new FileInfo(file);
        var stamp = info.Exists ? new Stamp(info.LastWriteTimeUtc, info.Length) : Stamp.Missing;

        if (_cache.TryGetValue(file, out var cached) && cached.Stamp == stamp)
            return cached.Map;

        var map = stamp == Stamp.Missing ? AreaMap.Empty : Read(file);
        _cache[file] = new CacheEntry(stamp, map);
        return map;
    }

    private AreaMap Read(string file)
    {
        try
        {
            var document = JsonSerializer.Deserialize<AreaMapDocument>(File.ReadAllText(file), JsonOptions);
            if (document?.Areas is not { Count: > 0 } declared)
            {
                _logger.LogWarning("Area map {File} declares no areas; scopes will compare as labels.", file);
                return AreaMap.Empty;
            }

            var areas = new Dictionary<string, AreaDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, entry) in declared)
            {
                if (string.IsNullOrWhiteSpace(name) || entry?.Paths is not { Count: > 0 } paths)
                {
                    _logger.LogWarning("Area '{Area}' in {File} declares no paths; skipped.", name, file);
                    continue;
                }

                var globs = paths
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .ToList();
                if (globs.Count == 0)
                    continue;

                areas[name.Trim()] = new AreaDefinition(name.Trim(), globs, ParseWeight(entry.Weight, name, file));
            }

            _logger.LogInformation("Loaded {Count} areas from {File}.", areas.Count, file);
            return new AreaMap(areas, file);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Never fatal. A broken map degrades to "no names known", which is the behaviour that
            // predates the map — the alternative is a typo in a JSON file stopping every dispatch
            // in the repo.
            _logger.LogWarning(ex, "Area map {File} could not be read; scopes will compare as labels.", file);
            return AreaMap.Empty;
        }
    }

    private AreaWeight ParseWeight(string? weight, string area, string file)
    {
        if (string.IsNullOrWhiteSpace(weight))
            return AreaWeight.Serialise;
        if (string.Equals(weight, "allow", StringComparison.OrdinalIgnoreCase))
            return AreaWeight.Allow;
        if (string.Equals(weight, "serialise", StringComparison.OrdinalIgnoreCase)
            || string.Equals(weight, "serialize", StringComparison.OrdinalIgnoreCase))
            return AreaWeight.Serialise;

        _logger.LogWarning(
            "Area '{Area}' in {File} declares unknown weight '{Weight}'; treated as serialise.",
            area, file, weight);
        return AreaWeight.Serialise;
    }

    private readonly record struct Stamp(DateTime WriteTimeUtc, long Length)
    {
        public static Stamp Missing { get; } = new(DateTime.MinValue, -1);
    }

    private sealed record CacheEntry(Stamp Stamp, AreaMap Map);

    private sealed class AreaMapDocument
    {
        [JsonPropertyName("areas")]
        public Dictionary<string, AreaEntry>? Areas { get; set; }
    }

    private sealed class AreaEntry
    {
        [JsonPropertyName("paths")]
        public List<string>? Paths { get; set; }

        [JsonPropertyName("weight")]
        public string? Weight { get; set; }
    }
}
