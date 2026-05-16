using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

public sealed class WatchdogCooldownStore
{
    private readonly ConcurrentDictionary<WatchdogCooldownKey, WatchdogCooldownState> _lastResponses = new();

    public bool TryRecord(Guid sessionId, string ruleName, DateTime utcNow, TimeSpan cooldown)
    {
        var key = new WatchdogCooldownKey(sessionId, ruleName);
        while (true)
        {
            if (!_lastResponses.TryGetValue(key, out var state))
                return _lastResponses.TryAdd(key, new WatchdogCooldownState(utcNow, Active: true));

            if (state.Active || utcNow - state.LastResponseAt < cooldown)
                return false;

            var next = new WatchdogCooldownState(utcNow, Active: true);
            if (_lastResponses.TryUpdate(key, next, state))
                return true;
        }
    }

    public void ClearActive(Guid sessionId)
    {
        foreach (var entry in _lastResponses)
        {
            if (entry.Key.SessionId != sessionId || !entry.Value.Active)
                continue;

            _lastResponses.TryUpdate(
                entry.Key,
                entry.Value with { Active = false },
                entry.Value);
        }
    }

    public void ClearActiveExcept(Guid sessionId, string activeRuleName)
    {
        foreach (var entry in _lastResponses)
        {
            if (entry.Key.SessionId != sessionId
                || !entry.Value.Active
                || entry.Key.RuleName.Equals(activeRuleName, StringComparison.Ordinal))
            {
                continue;
            }

            _lastResponses.TryUpdate(
                entry.Key,
                entry.Value with { Active = false },
                entry.Value);
        }
    }

    private sealed record WatchdogCooldownKey(Guid SessionId, string RuleName);

    private sealed record WatchdogCooldownState(DateTime LastResponseAt, bool Active);
}
