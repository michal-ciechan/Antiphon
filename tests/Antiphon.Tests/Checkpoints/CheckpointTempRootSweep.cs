using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

internal static class CheckpointTempSweepAssemblyHook
{
    [Before(Assembly)]
    public static void Sweep()
    {
        if (Environment.GetEnvironmentVariable("C804_ORPHAN_SWEEP_ROOT") is not null) return;
        Sweep(CheckpointCleanupDefaults.Context, Path.GetTempPath(), Console.Out);
    }

    public static CheckpointSweepReceipt Sweep(CheckpointCleanupContext context, string temp, TextWriter? output = null) =>
        new CheckpointTempRootSweep(context, temp).SweepOnce(output);

    [Before(Assembly)]
    public static void RecordSelectedRoster(AssemblyHookContext context)
    {
        var target = Environment.GetEnvironmentVariable("C804_ROSTER_FILE");
        if (string.IsNullOrWhiteSpace(target)) return;
        CheckpointRoster.Write(target, context.AllTests.Select(test => new CheckpointRoster.Case(
            test.Id, test.Metadata.TestDetails.ClassType, test.Metadata.TestDetails.MethodName,
            test.Metadata.TestDetails.TestName)));
    }
}

// Antiphon.Tests adds the compiled-case inventory in CheckpointRoster.Compiled.cs; this part is
// also linked into LifecycleHost, which records its own roster.
internal static partial class CheckpointRoster
{
    public sealed record Case(string Id, Type ClassType, string Method, string DisplayName);

    // AllTests still lists [Explicit] cases the wildcard usage filters never run, so a
    // roster that kept them could never join the TRX (55 Full cases on this source).
    public static bool IsExplicit(Type classType, string method) =>
        classType.IsDefined(typeof(ExplicitAttribute), inherit: true)
        || classType.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Any(candidate => candidate.Name == method && candidate.IsDefined(typeof(ExplicitAttribute), inherit: true));

    public static void Write(string target, IEnumerable<Case> tests)
    {
        var selected = tests.Where(test => !IsExplicit(test.ClassType, test.Method)).Select(test => new
        {
            id = test.Id,
            className = test.ClassType.FullName,
            method = test.Method,
            displayName = test.DisplayName,
        }).OrderBy(test => test.id).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, selected);
            stream.Flush(flushToDisk: true);
        }
        catch (IOException) when (File.Exists(target))
        {
            // A nested TUnit child inherits the observer environment. The first
            // assembly owns this roster; a later child cannot replace it.
        }
    }
}
