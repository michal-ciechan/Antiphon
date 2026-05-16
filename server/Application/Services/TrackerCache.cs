using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

public sealed class TrackerCache
{
    private readonly Dictionary<string, Task<IReadOnlyList<TrackedIssue>>> _entries = [];

    public Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(
        IIssueTracker tracker,
        IssueTrackerConfig config,
        CancellationToken ct) =>
        GetOrAdd(
            $"candidates:{ConfigKey(config)}",
            () => tracker.FetchCandidatesAsync(config, ct));

    public Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
        IIssueTracker tracker,
        IssueTrackerConfig config,
        IReadOnlyList<string> states,
        CancellationToken ct) =>
        GetOrAdd(
            $"states:{ConfigKey(config)}:{SetKey(states)}",
            () => tracker.FetchByStatesAsync(config, states, ct));

    public Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
        IIssueTracker tracker,
        IssueTrackerConfig config,
        IReadOnlyList<string> externalIds,
        CancellationToken ct) =>
        GetOrAdd(
            $"ids:{ConfigKey(config)}:{SetKey(externalIds)}",
            () => tracker.FetchByIdsAsync(config, externalIds, ct));

    private Task<IReadOnlyList<TrackedIssue>> GetOrAdd(
        string key,
        Func<Task<IReadOnlyList<TrackedIssue>>> factory)
    {
        if (_entries.TryGetValue(key, out var existing))
            return existing;

        var created = factory();
        _entries[key] = created;
        return created;
    }

    private static string ConfigKey(IssueTrackerConfig config) =>
        string.Join(
            "|",
            config.Kind,
            config.BaseUrl,
            config.ProjectKey,
            config.Repository,
            SetKey(config.ActiveStates),
            config.ApiKeyEnv,
            config.Jql,
            OptionsKey(config.Options));

    private static string SetKey(IEnumerable<string> values) =>
        string.Join(
            ",",
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Order(StringComparer.OrdinalIgnoreCase));

    private static string OptionsKey(IReadOnlyDictionary<string, string> options) =>
        string.Join(
            ",",
            options
                .Where(option => !string.IsNullOrWhiteSpace(option.Key))
                .Select(option => $"{option.Key.Trim()}={option.Value.Trim()}")
                .Order(StringComparer.Ordinal));
}
