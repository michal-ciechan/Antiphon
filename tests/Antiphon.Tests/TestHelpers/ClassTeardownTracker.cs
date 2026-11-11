using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Counts test classes whose After(Class) work has not finished. TUnit 1.44
/// (<c>TestLifecycleCoordinator.DecrementAndCheckAfterHooks</c>) decrements the class and assembly
/// counters together before it runs After(Class), so the assembly's last test can start
/// After(Assembly) while another class's After(Class) is still running. Run ae84478d g80: the
/// AgentTaskLandNotificationParkFollowUpTests teardown found <see cref="TestDbFixture"/> already
/// disposed. The assembly teardown waits, bounded, until every entered class has exited.
/// AfterEvery(Class) runs after the class's own After(Class) hooks, failed or not.
/// </summary>
internal sealed class ClassTeardownTracker
{
    internal static ClassTeardownTracker Default { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _open = new(StringComparer.Ordinal);
    private TaskCompletionSource _idle = Completed();

    public void Enter(string className)
    {
        lock (_gate)
        {
            if (_open.Count == 0)
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _open[className] = _open.GetValueOrDefault(className) + 1;
        }
    }

    public void Exit(string className)
    {
        lock (_gate)
        {
            if (!_open.TryGetValue(className, out var count))
                return;
            if (count > 1)
            {
                _open[className] = count - 1;
                return;
            }

            _open.Remove(className);
            if (_open.Count == 0)
                _idle.TrySetResult();
        }
    }

    /// <summary>Returns the classes still open when <paramref name="bound"/> expired, else empty.</summary>
    public async Task<IReadOnlyList<string>> WaitForIdleAsync(TimeSpan bound)
    {
        Task idle;
        lock (_gate)
            idle = _idle.Task;

        if (!idle.IsCompleted)
            await Task.WhenAny(idle, Task.Delay(bound)).ConfigureAwait(false);

        lock (_gate)
            return _open.Count == 0 ? [] : _open.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        done.TrySetResult();
        return done;
    }

    internal static string NameOf(ClassHookContext context) =>
        context.ClassType.FullName ?? context.ClassType.Name;
}

/// <summary>Feeds <see cref="ClassTeardownTracker.Default"/> from TUnit's global class hooks.</summary>
internal static class ClassTeardownTrackerHooks
{
    [BeforeEvery(Class)]
    public static void Enter(ClassHookContext context) =>
        ClassTeardownTracker.Default.Enter(ClassTeardownTracker.NameOf(context));

    [AfterEvery(Class)]
    public static void Exit(ClassHookContext context) =>
        ClassTeardownTracker.Default.Exit(ClassTeardownTracker.NameOf(context));
}
