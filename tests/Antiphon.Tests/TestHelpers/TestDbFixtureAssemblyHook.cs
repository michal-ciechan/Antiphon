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

    [After(Assembly)]
    public static Task DisposeAsync() => TestDbFixture.DisposeAsync();
}
