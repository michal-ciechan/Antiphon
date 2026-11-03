using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1687 D-1: runs the forced-Windows read-only cleanup witness so a change to how
/// <c>Clear-C487OwnedResultReadOnly</c> lists directories cannot silently break it again.
/// The witness fakes the Windows tree, so it runs on Linux and Windows alike.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C487ReadOnlyCleanupWitnessTests
{
    [Test, Timeout(90_000)]
    public Task C487_WindowsReadOnlyWitness(CancellationToken token) =>
        ScriptHarnessProcess.RunAsync("c487-readonly-windows-witness.ps1", "C487W", nameof(C487_WindowsReadOnlyWitness),
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures", "c487-readonly-windows-witness.ps1"),
            new ScriptHarnessOptions(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10)), token,
            result =>
            {
                var output = result.Stdout + result.Stderr;
                result.ExitCode.ShouldBe(0, output);
                output.ShouldContain("C487 cleanup clearedReadOnlyFiles=3", Case.Sensitive, output);
                output.ShouldContain("C487 Windows cleanup witness passed", Case.Sensitive, output);
            });
}
