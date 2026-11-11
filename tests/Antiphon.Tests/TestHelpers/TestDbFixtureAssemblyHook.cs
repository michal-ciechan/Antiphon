using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Worker dispatch and the assembly attributes stay in this project. The resilience
/// mirror links <see cref="TestDbFixture"/> and calls it from its own hook (CARD-1829).
/// </summary>
internal static class TestDbFixtureAssemblyHook
{
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

    // Run ae84478d g80: an After(Class) clone drop overtaken by this teardown. A class teardown
    // (harness dispose, clone drop) is seconds; past this the fixture goes anyway and names them.
    internal static readonly TimeSpan ClassTeardownBound = TimeSpan.FromSeconds(60);

    [After(Assembly)]
    public static Task DisposeAsync() =>
        DisposeAfterClassTeardownsAsync(ClassTeardownTracker.Default, TestDbFixture.DisposeAsync, ClassTeardownBound);

    /// <summary>
    /// TUnit 1.44 can start After(Assembly) while another class's After(Class) still runs; wait for
    /// those before the shared store goes, so their clone drops do not hit a disposed fixture.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> DisposeAfterClassTeardownsAsync(
        ClassTeardownTracker tracker, Func<Task> dispose, TimeSpan bound)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var open = await tracker.WaitForIdleAsync(bound).ConfigureAwait(false);
        if (open.Count > 0)
            Console.Error.WriteLine(
                $"ANTIPHON_FIXTURE_TEARDOWN class teardowns still open after {timer.ElapsedMilliseconds} ms; disposing anyway: {string.Join(", ", open)}");
        else if (timer.ElapsedMilliseconds >= 100)
            Console.Error.WriteLine(
                $"ANTIPHON_FIXTURE_TEARDOWN waited {timer.ElapsedMilliseconds} ms for open class teardowns");
        await dispose().ConfigureAwait(false);
        return open;
    }
}
