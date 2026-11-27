using Antiphon.Tests.TestHelpers;

// The .NET host calls StartupHook.Initialize before it enters Microsoft.Testing.Platform.
// Only the fixture-owned retirement children install this hook.
internal static class StartupHook
{
    internal const string Variable = "DOTNET_STARTUP_HOOKS";

    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable(Variable) != typeof(StartupHook).Assembly.Location
            || Environment.GetEnvironmentVariable(WorktreeRetirementCrashWorker.Marker) is null)
            return;

        using (TestTrace.ChunkSpan("retirement worker direct startup"))
            Environment.SetEnvironmentVariable(Variable, null);

        WorktreeRetirementCrashWorker.RunBeforeDiscoveryAsync().GetAwaiter().GetResult();
    }
}
