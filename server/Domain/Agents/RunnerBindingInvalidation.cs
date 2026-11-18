using System.Collections.Concurrent;

namespace Antiphon.Server.Domain.Agents;

/// <summary>
/// CARD-1987. A binding-column write tells every live routing client to drop its cache.
/// Listeners are weak so a test-constructed client can be collected.
/// </summary>
internal static class RunnerBindingInvalidation
{
    private static readonly ConcurrentDictionary<Guid, WeakReference<Action<Guid?>>> Listeners = new();

    internal static void Subscribe(Action<Guid?> onWrite)
    {
        ArgumentNullException.ThrowIfNull(onWrite);
        Prune();
        Listeners[Guid.NewGuid()] = new WeakReference<Action<Guid?>>(onWrite);
    }

    /// <summary>Null drops every cached session. An id drops that session only.</summary>
    internal static void Notify(Guid? sessionId)
    {
        foreach (var pair in Listeners)
        {
            if (!pair.Value.TryGetTarget(out var listener))
            {
                Listeners.TryRemove(pair.Key, out _);
                continue;
            }

            listener(sessionId);
        }
    }

    private static void Prune()
    {
        foreach (var pair in Listeners)
        {
            if (!pair.Value.TryGetTarget(out _))
                Listeners.TryRemove(pair.Key, out _);
        }
    }
}
