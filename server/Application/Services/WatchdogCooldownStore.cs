using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

public sealed class WatchdogCooldownStore
{
    private readonly ConcurrentDictionary<WatchdogCooldownKey, WatchdogCooldownState> _lastResponses = new();
    private readonly ConcurrentDictionary<Guid, int> _unsafeAnswerHolds = new();

    // A single empty redraw can follow a frame with a typed answer. Require two consecutive
    // complete empty modal observations before a rendered-only answer hold is released.
    public void HoldUnsafeAnswer(Guid sessionId) => _unsafeAnswerHolds[sessionId] = 0;

    public bool ConfirmUnsafeAnswerCleared(Guid sessionId)
    {
        if (!_unsafeAnswerHolds.ContainsKey(sessionId))
            return true;
        if (_unsafeAnswerHolds.AddOrUpdate(sessionId, 1, (_, count) => count + 1) < 2)
            return false;
        _unsafeAnswerHolds.TryRemove(sessionId, out _);
        return true;
    }

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
