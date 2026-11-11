using System.Diagnostics;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Worker dispatch and the assembly attributes stay in this project. The resilience
/// mirror links <see cref="TestDbFixture"/> and calls it from its own hook (CARD-1829).
/// </summary>
internal static class TestDbFixtureAssemblyHook
{
    /// <summary>
    /// Run ae84478d g80: the longest wait for an open After(Class) before the shared store is
    /// disposed anyway. A class still open at the bound is named in the output.
    /// </summary>
    internal static readonly TimeSpan ClassTeardownBound = TimeSpan.FromMinutes(2);

    [Before(Assembly)]
    public static async Task InitializeAsync(AssemblyHookContext context)
    {
        // CARD-0646: every worker mode runs on a parent-owned connection and exits here, before
        // the warm-up below, so no worker child ever starts the shared store.
        if (TestWorkerModes.Requested() is { } worker)
        {
            await TestWorkerModes.RunAndExitAsync(worker.Mode, worker.Payload);
            return;
        }

        await TestDbFixture.InitializeAsync(context);
    }

    [BeforeEvery(Class)]
    public static void EnterClass(ClassHookContext context) =>
        ClassTeardownTracker.Default.Enter(context.ClassType.FullName ?? context.ClassType.Name);

    [AfterEvery(Class)]
    public static void ExitClass(ClassHookContext context) =>
        ClassTeardownTracker.Default.Exit(context.ClassType.FullName ?? context.ClassType.Name);

    [After(Assembly)]
    public static Task DisposeAsync() =>
        DisposeAfterClassTeardownsAsync(ClassTeardownTracker.Default, TestDbFixture.DisposeAsync, ClassTeardownBound);

    /// <summary>
    /// TUnit 1.44 can run After(Assembly) for the last test while another class's After(Class)
    /// is still in flight. Disposing the store under it fails that class's clone drop with
    /// ObjectDisposedException (run ae84478d g80), so wait for open classes, then dispose.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> DisposeAfterClassTeardownsAsync(
        ClassTeardownTracker tracker, Func<Task> dispose, TimeSpan bound)
    {
        var waited = Stopwatch.StartNew();
        var open = await tracker.WaitForIdleAsync(bound).ConfigureAwait(false);
        if (open.Count > 0)
            Console.WriteLine(
                $"ClassTeardownTracker: disposing the shared store after {waited.ElapsedMilliseconds} ms with After(Class) still open: {string.Join(", ", open)}");
        else if (waited.ElapsedMilliseconds >= 50)
            Console.WriteLine(
                $"ClassTeardownTracker: waited {waited.ElapsedMilliseconds} ms for open After(Class) hooks before disposing the shared store");
        await dispose().ConfigureAwait(false);
        return open;
    }
}
