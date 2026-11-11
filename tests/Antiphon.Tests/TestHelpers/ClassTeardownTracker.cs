namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Counts test classes whose After(Class) work has not finished. TUnit 1.44 can start
/// After(Assembly) while another class's After(Class) is still running (run ae84478d g80), so
/// the assembly teardown waits here before it disposes shared fixtures. A class enters at
/// BeforeEvery(Class) and exits at AfterEvery(Class), which TUnit runs after that class's own
/// After(Class) hooks, including one that threw.
/// </summary>
internal sealed class ClassTeardownTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _open = new(StringComparer.Ordinal);
    private TaskCompletionSource? _idle;

    internal static ClassTeardownTracker Default { get; } = new();

    public void Enter(string className)
    {
        lock (_gate)
            _open[className] = _open.GetValueOrDefault(className) + 1;
    }

    public void Exit(string className)
    {
        TaskCompletionSource? idle = null;
        lock (_gate)
        {
            if (!_open.TryGetValue(className, out var count))
                return;
            if (count > 1)
                _open[className] = count - 1;
            else
                _open.Remove(className);
            if (_open.Count == 0)
                (idle, _idle) = (_idle, null);
        }

        idle?.TrySetResult();
    }

    /// <summary>
    /// Completes when no class is open, or at <paramref name="bound"/>. Returns the classes still
    /// open at that moment, so a teardown that never closed is named rather than waited on forever.
    /// </summary>
    public async Task<IReadOnlyList<string>> WaitForIdleAsync(TimeSpan bound)
    {
        Task idle;
        lock (_gate)
        {
            if (_open.Count == 0)
                return [];
            _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            idle = _idle.Task;
        }

        await Task.WhenAny(idle, Task.Delay(bound)).ConfigureAwait(false);
        lock (_gate)
            return [.. _open.Keys.Order(StringComparer.Ordinal)];
    }
}
